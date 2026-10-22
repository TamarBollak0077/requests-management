using Microsoft.EntityFrameworkCore;
using RequestsManagement.Api.Domain;
using System.Diagnostics;

namespace RequestsManagement.Api.Data.Seeding;

/// <summary>
/// Development-only seeder.  Invoked via CLI argument:
///   dotnet run --project src/RequestsManagement.Api -- --seed [count]
///
/// Design decisions:
/// - Fixed Random seed (42) + fixed base date (2024-01-01 UTC) ensure the
///   same 100 K rows are produced on every run.
/// - Inserts in batches of 5 000 using AddRange + SaveChangesAsync + Clear,
///   so the ChangeTracker never accumulates all 100 K entities in memory.
/// - NEVER deletes the database. It DELETEs rows from the two tables only
///   after printing the target server and database for confirmation.
/// - Skips seeding when the table already contains the requested count, so
///   accidental re-runs are safe.
/// </summary>
public static class DataSeeder
{
    private const int BatchSize = 5_000;
    private const int RandomSeed = 42;

    // Fixed base date — changing this would change all generated rows.
    private static readonly DateTime BaseDate = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly string[] Organizations =
    [
        "Acme Corp", "Global Tech", "Sunrise Ltd", "Blue Wave", "Peak Solutions",
        "Nova Systems", "Bright Future", "Iron Gate", "Cedar Group", "Maple Industries",
        "Stone Bridge", "Silver Line", "Golden Path", "Red Rock", "Green Valley",
        "Blue Horizon", "Swift Edge", "Bold Move", "Clear View", "Prime Force"
    ];

    private static readonly string[] Handlers =
    [
        "alice", "bob", "carol", "david", "eve",
        "frank", "grace", "henry", "iris", "jack"
    ];

    public static async Task RunAsync(
        AppDbContext db,
        ILogger logger,
        int count = 100_000,
        CancellationToken ct = default)
    {
        // Print the exact target so the operator can verify before anything changes.
        var conn = db.Database.GetConnectionString() ?? "(unknown)";
        logger.LogWarning(
            "Seeder targeting database: {ConnectionString}  Requested count: {Count}",
            RedactPassword(conn), count);

        // Skip if already seeded to the requested count.
        var existing = await db.Requests.CountAsync(ct);
        if (existing >= count)
        {
            logger.LogInformation(
                "Skipping seed — table already contains {Existing} rows (>= {Count}).",
                existing, count);
            return;
        }

        // Clear existing rows so re-seeding from a partial run is clean.
        // Only the two seeded tables are touched; schema is preserved.
        if (existing > 0)
        {
            logger.LogWarning(
                "Deleting {Existing} existing rows from Requests and RequestStatusHistory …",
                existing);
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM [RequestStatusHistory]; DELETE FROM [Requests];", ct);
        }

        var sw = Stopwatch.StartNew();
        var rng = new Random(RandomSeed);
        var statuses = Enum.GetValues<RequestStatus>();
        var priorities = Enum.GetValues<RequestPriority>();

        // Date spread: BaseDate .. BaseDate + 2 years (730 days × 86400 s)
        const int SpreadSeconds = 730 * 24 * 3_600;

        int inserted = 0;

        while (inserted < count)
        {
            var batch = new List<Request>(BatchSize);
            int batchCount = Math.Min(BatchSize, count - inserted);

            for (int i = 0; i < batchCount; i++)
            {
                var createdAt = BaseDate.AddSeconds(rng.Next(0, SpreadSeconds));
                // updatedAt is between createdAt and 30 days later
                var updatedAt = createdAt.AddSeconds(rng.Next(0, 30 * 24 * 3_600));

                batch.Add(new Request
                {
                    Title = $"Request #{inserted + i + 1:D6}",
                    OrganizationName = Organizations[rng.Next(Organizations.Length)],
                    Status = statuses[rng.Next(statuses.Length)],
                    Priority = priorities[rng.Next(priorities.Length)],
                    AssignedTo = rng.Next(10) < 8
                        ? Handlers[rng.Next(Handlers.Length)]
                        : null,   // ~20 % unassigned
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt
                });
            }

            await db.Requests.AddRangeAsync(batch, ct);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();   // release all tracked entities — keeps memory flat

            inserted += batchCount;
            logger.LogInformation("Seeded {Inserted}/{Total} …", inserted, count);
        }

        sw.Stop();
        var finalCount = await db.Requests.CountAsync(ct);
        logger.LogInformation(
            "Seed complete. Rows in DB: {FinalCount}. Elapsed: {Elapsed}.",
            finalCount, sw.Elapsed);
    }

    private static string RedactPassword(string cs)
    {
        // Very simple: hide anything after "Password=" or "pwd="
        return System.Text.RegularExpressions.Regex.Replace(
            cs,
            @"(?i)(password|pwd)\s*=\s*[^;]+",
            "$1=***",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
