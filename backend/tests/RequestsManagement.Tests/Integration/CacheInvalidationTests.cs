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
/// T-606 — Cache invalidation integration tests.
/// Verifies that GET /api/requests/summary reflects status updates
/// immediately after a successful PATCH, without waiting for TTL expiry.
/// </summary>
[Collection("Database")]
public sealed class CacheInvalidationTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public CacheInvalidationTests(ApiFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public async Task InitializeAsync() => await _factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetSummary_AfterSuccessfulStatusUpdate_ReflectsNewStatusImmediately()
    {
        // ── 1. Seed two requests: both New ────────────────────────────────────
        int    requestId;
        string rowVersion;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = new Request
            {
                Title            = "Cache-Test Request",
                OrganizationName = "Cache Org",
                Status           = RequestStatus.New,
                Priority         = RequestPriority.Medium,
                CreatedAt        = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt        = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            };
            db.Requests.Add(req);
            await db.SaveChangesAsync();
            requestId  = req.Id;
            rowVersion = Convert.ToBase64String(req.RowVersion);
        }

        // ── 2. GET /api/requests/summary — populates the cache ────────────────
        var summaryBefore = await _client.GetAsync("/api/requests/summary");
        Assert.Equal(HttpStatusCode.OK, summaryBefore.StatusCode);

        var bodyBefore  = await summaryBefore.Content.ReadAsStringAsync();
        var dtoBefore   = JsonSerializer.Deserialize<SummaryDto>(bodyBefore, JsonOpts);
        Assert.NotNull(dtoBefore);

        int newCountBefore        = CountForStatus(dtoBefore, "New");
        int inProgressCountBefore = CountForStatus(dtoBefore, "InProgress");
        int totalBefore           = TotalCount(dtoBefore);

        // ── 3. PATCH the seeded request: New → InProgress ─────────────────────
        var patchResponse = await _client.PatchAsJsonAsync(
            $"/api/requests/{requestId}/status",
            new { newStatus = "InProgress", rowVersion, changedBy = "cache-tester" });

        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);

        // ── 4. GET /api/requests/summary again immediately ────────────────────
        // No Thread.Sleep — invalidation must be synchronous (cache.Remove after Commit).
        var summaryAfter = await _client.GetAsync("/api/requests/summary");
        Assert.Equal(HttpStatusCode.OK, summaryAfter.StatusCode);

        var bodyAfter = await summaryAfter.Content.ReadAsStringAsync();
        var dtoAfter  = JsonSerializer.Deserialize<SummaryDto>(bodyAfter, JsonOpts);
        Assert.NotNull(dtoAfter);

        int newCountAfter        = CountForStatus(dtoAfter, "New");
        int inProgressCountAfter = CountForStatus(dtoAfter, "InProgress");
        int totalAfter           = TotalCount(dtoAfter);

        // ── 5. Assert counts changed correctly ────────────────────────────────
        // New count must decrease by 1; InProgress count must increase by 1.
        Assert.Equal(newCountBefore        - 1, newCountAfter);
        Assert.Equal(inProgressCountBefore + 1, inProgressCountAfter);

        // Total must stay the same — no requests created or deleted.
        Assert.Equal(totalBefore, totalAfter);
    }

    [Fact]
    public async Task GetSummary_AfterRejectedStatusUpdate_CountsUnchanged()
    {
        // ── 1. Seed a Completed request (terminal — no valid transitions) ──────
        int    requestId;
        string rowVersion;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var req = new Request
            {
                Title            = "Completed-Cache Request",
                OrganizationName = "Cache Org",
                Status           = RequestStatus.Completed,
                Priority         = RequestPriority.Low,
                CreatedAt        = new DateTime(2024, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt        = new DateTime(2024, 3, 2, 0, 0, 0, DateTimeKind.Utc),
            };
            db.Requests.Add(req);
            await db.SaveChangesAsync();
            requestId  = req.Id;
            rowVersion = Convert.ToBase64String(req.RowVersion);
        }

        // ── 2. GET summary — populate cache ───────────────────────────────────
        var summaryBefore = await _client.GetAsync("/api/requests/summary");
        Assert.Equal(HttpStatusCode.OK, summaryBefore.StatusCode);

        var dtoBefore = JsonSerializer.Deserialize<SummaryDto>(
            await summaryBefore.Content.ReadAsStringAsync(), JsonOpts);
        Assert.NotNull(dtoBefore);
        int totalBefore      = TotalCount(dtoBefore);
        int completedBefore  = CountForStatus(dtoBefore, "Completed");

        // ── 3. Attempt forbidden transition: Completed → InProgress ──────────
        var patchResponse = await _client.PatchAsJsonAsync(
            $"/api/requests/{requestId}/status",
            new { newStatus = "InProgress", rowVersion, changedBy = "cache-tester" });

        Assert.Equal(HttpStatusCode.BadRequest, patchResponse.StatusCode);

        // ── 4. GET summary again ──────────────────────────────────────────────
        var summaryAfter = await _client.GetAsync("/api/requests/summary");
        Assert.Equal(HttpStatusCode.OK, summaryAfter.StatusCode);

        var dtoAfter = JsonSerializer.Deserialize<SummaryDto>(
            await summaryAfter.Content.ReadAsStringAsync(), JsonOpts);
        Assert.NotNull(dtoAfter);
        int totalAfter     = TotalCount(dtoAfter);
        int completedAfter = CountForStatus(dtoAfter, "Completed");

        // ── 5. Assert no change ───────────────────────────────────────────────
        Assert.Equal(totalBefore,     totalAfter);
        Assert.Equal(completedBefore, completedAfter);
    }

    private static int CountForStatus(SummaryDto dto, string statusName) =>
        dto.ByStatus.FirstOrDefault(s => s.Status == statusName)?.Count ?? 0;

    private static int TotalCount(SummaryDto dto) =>
        dto.ByStatus.Sum(s => s.Count);

    private sealed record SummaryDto(
        List<StatusCountItem> ByStatus,
        List<PriorityCountItem> ByPriority);

    private sealed record StatusCountItem(string Status, int Count);

    private sealed record PriorityCountItem(string Priority, int Count);
}
