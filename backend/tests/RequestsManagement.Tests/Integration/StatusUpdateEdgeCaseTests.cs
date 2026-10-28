using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RequestsManagement.Api.Data;
using RequestsManagement.Api.Domain;
using RequestsManagement.Tests.Infrastructure;

namespace RequestsManagement.Tests.Integration;

/// <summary>
/// T-605 — Integration tests for HTTP 404, HTTP 400 (invalid transition),
/// and bulk partial success scenarios.
/// </summary>
[Collection("Database")]
public sealed class StatusUpdateEdgeCaseTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public StatusUpdateEdgeCaseTests(ApiFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public async Task InitializeAsync() => await _factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PatchStatus_NonExistentId_Returns404()
    {
        // A valid-looking rowVersion that satisfies the DTO validator (8 bytes Base64).
        var fakeRowVersion = Convert.ToBase64String(new byte[8]);

        var response = await _client.PatchAsJsonAsync(
            "/api/requests/99999999/status",
            new { newStatus = "InProgress", rowVersion = fakeRowVersion, changedBy = "tester" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PatchStatus_InvalidTransition_Returns400AndLeavesStatusUnchanged()
    {
        // ── 1. Seed a Completed request ───────────────────────────────────────
        int    requestId;
        string rowVersion;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var request = new Request
            {
                Title            = "Completed Request",
                OrganizationName = "Test Org",
                Status           = RequestStatus.Completed,
                Priority         = RequestPriority.Low,
                CreatedAt        = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt        = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            };
            db.Requests.Add(request);
            await db.SaveChangesAsync();
            requestId  = request.Id;
            rowVersion = Convert.ToBase64String(request.RowVersion);
        }

        // ── 2. Attempt a forbidden transition: Completed → InProgress ─────────
        var response = await _client.PatchAsJsonAsync(
            $"/api/requests/{requestId}/status",
            new { newStatus = "InProgress", rowVersion, changedBy = "tester" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The 400 body should be ProblemDetails with a meaningful detail message.
        var body   = await response.Content.ReadAsStringAsync();
        var problem = JsonSerializer.Deserialize<ProblemDetailsDto>(body, JsonOpts);
        Assert.NotNull(problem?.Detail);
        Assert.Contains("Completed", problem.Detail, StringComparison.OrdinalIgnoreCase);

        // ── 3. Confirm DB is unchanged ────────────────────────────────────────
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var req = await verifyDb.Requests.AsNoTracking().SingleAsync(r => r.Id == requestId);
        Assert.Equal(RequestStatus.Completed, req.Status);

        // Confirm no audit row was written.
        var historyCount = await verifyDb.RequestStatusHistory
            .AsNoTracking()
            .CountAsync(h => h.RequestId == requestId);
        Assert.Equal(0, historyCount);
    }

    [Fact]
    public async Task BulkUpdateStatus_MixedBatch_ReturnsCorrectOutcomesAndPersistsOnlySuccesses()
    {
        // ── 1. Seed three requests ────────────────────────────────────────────
        //    A: New  — valid update target
        //    B: New  — will be used with a deliberately stale rowVersion (Conflict)
        //    C: New  — will be updated first to obtain a fresh rowVersion, then
        //              submitted in the bulk with that rowVersion to trigger Conflict
        //    plus a nonexistent ID for the NotFound case

        int    idA, idB;
        string rowVersionA, rowVersionB;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reqA = new Request
            {
                Title = "Bulk-A", OrganizationName = "Org",
                Status = RequestStatus.New, Priority = RequestPriority.Medium,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            };
            var reqB = new Request
            {
                Title = "Bulk-B", OrganizationName = "Org",
                Status = RequestStatus.New, Priority = RequestPriority.Medium,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            };
            db.Requests.AddRange(reqA, reqB);
            await db.SaveChangesAsync();

            idA = reqA.Id;  rowVersionA = Convert.ToBase64String(reqA.RowVersion);
            idB = reqB.Id;  rowVersionB = Convert.ToBase64String(reqB.RowVersion);
        }

        // Advance reqB so rowVersionB becomes stale, producing a Conflict in the bulk call.
        var preUpdate = await _client.PatchAsJsonAsync(
            $"/api/requests/{idB}/status",
            new { newStatus = "InProgress", rowVersion = rowVersionB, changedBy = "setup" });
        Assert.Equal(HttpStatusCode.OK, preUpdate.StatusCode);

        // ── 2. Submit a 3-item bulk: Updated, NotFound, Conflict ──────────────
        // newStatus = "Waiting": New→Waiting and InProgress→Waiting are both valid
        // transitions, so B's stale rowVersion triggers Conflict rather than
        // InvalidTransition (which would fire first if the transition were forbidden).
        const int nonExistentId = 88888888;
        var bulkPayload = new
        {
            newStatus = "Waiting",
            changedBy = "bulk-tester",
            items = new[]
            {
                new { id = idA,          rowVersion = rowVersionA },  // valid → Updated
                new { id = nonExistentId, rowVersion = rowVersionA }, // no such row → NotFound
                new { id = idB,          rowVersion = rowVersionB },  // stale token → Conflict
            },
        };

        var bulkResponse = await _client.PostAsJsonAsync("/api/requests/status/bulk", bulkPayload);
        Assert.Equal(HttpStatusCode.OK, bulkResponse.StatusCode);

        var bulkBody = await bulkResponse.Content.ReadAsStringAsync();
        var result   = JsonSerializer.Deserialize<BulkResultDto>(bulkBody, JsonOpts);
        Assert.NotNull(result);

        // ── 3. Assert summary counts ──────────────────────────────────────────
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(2, result.Failed);
        Assert.Equal(3, result.Results.Count);

        // ── 4. Assert per-item outcomes ───────────────────────────────────────
        var resultA = result.Results.Single(r => r.Id == idA);
        Assert.Equal("Updated", resultA.Outcome);
        Assert.NotNull(resultA.RowVersion); // new rowVersion issued

        var resultNotFound = result.Results.Single(r => r.Id == nonExistentId);
        Assert.Equal("NotFound", resultNotFound.Outcome);

        var resultB = result.Results.Single(r => r.Id == idB);
        Assert.Equal("Conflict", resultB.Outcome);
        Assert.NotNull(resultB.RowVersion); // current server rowVersion returned

        // ── 5. Verify DB state ────────────────────────────────────────────────
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        // A was updated successfully.
        var reqADb = await verifyDb.Requests.AsNoTracking().SingleAsync(r => r.Id == idA);
        Assert.Equal(RequestStatus.Waiting, reqADb.Status);

        // B was not updated by the bulk (Conflict); it stays at InProgress from the pre-update.
        var reqBDb = await verifyDb.Requests.AsNoTracking().SingleAsync(r => r.Id == idB);
        Assert.Equal(RequestStatus.InProgress, reqBDb.Status);

        // ── 6. Verify audit rows ──────────────────────────────────────────────
        // A: 1 audit row from the bulk call (New → Waiting).
        var auditA = await verifyDb.RequestStatusHistory
            .AsNoTracking()
            .Where(h => h.RequestId == idA)
            .ToListAsync();
        Assert.Single(auditA);
        Assert.Equal(RequestStatus.New,     auditA[0].PreviousStatus);
        Assert.Equal(RequestStatus.Waiting, auditA[0].NewStatus);
        Assert.Equal("bulk-tester",            auditA[0].ChangedBy);

        // B: 1 audit row from the pre-update call (setup), none from the failed bulk item.
        var auditB = await verifyDb.RequestStatusHistory
            .AsNoTracking()
            .Where(h => h.RequestId == idB)
            .ToListAsync();
        Assert.Single(auditB);
        Assert.Equal("setup", auditB[0].ChangedBy);
    }

    private sealed record ProblemDetailsDto(string? Detail, string? Title);

    private sealed record BulkResultDto(
        int Succeeded,
        int Failed,
        List<BulkItemResultDto> Results);

    private sealed record BulkItemResultDto(
        int Id,
        string Outcome,
        string? RowVersion,
        string? Detail);
}
