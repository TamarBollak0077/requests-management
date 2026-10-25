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

[Collection("Database")]
public sealed class ConcurrencyTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ConcurrencyTests(ApiFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public async Task InitializeAsync() => await _factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PatchStatus_TwoConsecutiveUpdates_BothSucceedAndWriteTwoAuditRows()
    {
        // ── 1. Seed one New request ───────────────────────────────────────────
        int    requestId;
        string originalRowVersionBase64;

        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var request = new Request
            {
                Title            = "Two-Step Update Request",
                OrganizationName = "Test Org",
                Status           = RequestStatus.New,
                Priority         = RequestPriority.Medium,
                CreatedAt        = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt        = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            };
            db.Requests.Add(request);
            await db.SaveChangesAsync();

            requestId = request.Id;
            originalRowVersionBase64 = Convert.ToBase64String(request.RowVersion);
        }

        // ── 2. First PATCH: New → InProgress ──────────────────────────────────
        var firstResponse = await _client.PatchAsJsonAsync(
            $"/api/requests/{requestId}/status",
            new { newStatus = "InProgress", rowVersion = originalRowVersionBase64, changedBy = "test-user" });

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // The 200 body contains the new RowVersion (UpdateStatusResultDto).
        var firstBody       = await firstResponse.Content.ReadAsStringAsync();
        var firstDto        = JsonSerializer.Deserialize<PatchSuccessDto>(firstBody, JsonOpts)!;
        var updatedRowVersion1 = firstDto.RowVersion;

        Assert.NotEqual(originalRowVersionBase64, updatedRowVersion1);

        // ── 3. Second PATCH: InProgress → Waiting with the UPDATED rowVersion ─
        var secondResponse = await _client.PatchAsJsonAsync(
            $"/api/requests/{requestId}/status",
            new { newStatus = "Waiting", rowVersion = updatedRowVersion1, changedBy = "test-user" });

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var secondBody      = await secondResponse.Content.ReadAsStringAsync();
        var secondDto       = JsonSerializer.Deserialize<PatchSuccessDto>(secondBody, JsonOpts)!;
        var updatedRowVersion2 = secondDto.RowVersion;

        Assert.NotEqual(updatedRowVersion1, updatedRowVersion2);

        // ── 4. Verify database state ──────────────────────────────────────────
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var freshRequest = await verifyDb.Requests
            .AsNoTracking()
            .SingleAsync(r => r.Id == requestId);

        // Order by Id (auto-increment) so the two rows are in insertion order.
        var historyRows = await verifyDb.RequestStatusHistory
            .AsNoTracking()
            .Where(h => h.RequestId == requestId)
            .OrderBy(h => h.Id)
            .ToListAsync();

        Assert.Equal(RequestStatus.Waiting, freshRequest.Status);
        Assert.NotEqual(originalRowVersionBase64, Convert.ToBase64String(freshRequest.RowVersion));

        Assert.Equal(2, historyRows.Count);

        Assert.Equal(RequestStatus.New,        historyRows[0].PreviousStatus);
        Assert.Equal(RequestStatus.InProgress, historyRows[0].NewStatus);

        Assert.Equal(RequestStatus.InProgress, historyRows[1].PreviousStatus);
        Assert.Equal(RequestStatus.Waiting,    historyRows[1].NewStatus);
    }

    [Fact]
    public async Task PatchStatus_SuccessThenStaleRowVersion_Returns409AndPreservesOneAuditRow()
    {
        // ── 1. Seed one New request ───────────────────────────────────────────
        int    requestId;
        string originalRowVersionBase64;

        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var request = new Request
            {
                Title            = "Concurrency Test Request",
                OrganizationName = "Test Org",
                Status           = RequestStatus.New,
                Priority         = RequestPriority.Medium,
                CreatedAt        = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt        = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            };
            db.Requests.Add(request);
            await db.SaveChangesAsync();

            // After SaveChanges, EF reads back the SQL Server-generated rowversion.
            requestId = request.Id;
            originalRowVersionBase64 = Convert.ToBase64String(request.RowVersion);
        }

        // ── 2. First PATCH: New → InProgress with the fresh rowVersion ────────
        // The transition is allowed; the rowVersion matches the DB — expect 200.
        var firstResponse = await _client.PatchAsJsonAsync(
            $"/api/requests/{requestId}/status",
            new
            {
                newStatus  = "InProgress",
                rowVersion = originalRowVersionBase64,
                changedBy  = "test-user",
            });

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // ── 3. Second PATCH: InProgress → Waiting with the STALE rowVersion ───
        // The transition InProgress → Waiting is allowed, but the rowVersion is
        // now outdated.  SQL Server's implicit WHERE rowversion = @stale matches
        // 0 rows → DbUpdateConcurrencyException → service returns 409.
        var secondResponse = await _client.PatchAsJsonAsync(
            $"/api/requests/{requestId}/status",
            new
            {
                newStatus  = "Waiting",
                rowVersion = originalRowVersionBase64,   // deliberately stale
                changedBy  = "test-user",
            });

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        // ── 4. Verify database state with a fresh, untracked DbContext ─────────
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var freshRequest = await verifyDb.Requests
            .AsNoTracking()
            .SingleAsync(r => r.Id == requestId);

        var historyRows = await verifyDb.RequestStatusHistory
            .AsNoTracking()
            .Where(h => h.RequestId == requestId)
            .ToListAsync();

        // Only the first (successful) update must have been applied.
        Assert.Equal(RequestStatus.InProgress, freshRequest.Status);

        // SQL Server incremented rowversion on the first UPDATE; it must differ.
        Assert.NotEqual(
            originalRowVersionBase64,
            Convert.ToBase64String(freshRequest.RowVersion));

        // The 409 path rolls back its transaction — no orphaned audit row.
        Assert.Single(historyRows);
        Assert.Equal(RequestStatus.New,        historyRows[0].PreviousStatus);
        Assert.Equal(RequestStatus.InProgress, historyRows[0].NewStatus);
    }

    // Captures only the fields this test class needs from UpdateStatusResultDto.
    private sealed record PatchSuccessDto(string RowVersion);
}
