using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RequestsManagement.Api.Data;

namespace RequestsManagement.Tests.Infrastructure;

/// <summary>
/// Shared WebApplicationFactory for all database integration tests.
/// Registered as a collection fixture so one instance is reused across
/// all test classes in the "Database" collection (no parallel DB access).
///
/// Lifecycle:
///   InitializeAsync  — runs EF migrations once against RequestsManagement_Tests.
///   ResetAsync       — called by each test class before its tests; deletes all
///                      rows in FK order so every class starts from a clean slate.
///   DisposeAsync     — no-op; the inherited IDisposable.Dispose() handles teardown.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Override connection string before any service is registered.
        // GetTestConnectionString() validates the target DB name and throws
        // immediately if it is not RequestsManagement_Tests.
        builder.UseSetting("ConnectionStrings:DefaultConnection", GetTestConnectionString());

        // Use "Testing" environment so Swagger middleware is not registered
        // and Development-only behaviour (verbose EF logging) is suppressed.
        builder.UseEnvironment("Testing");
    }

    // ── IAsyncLifetime ────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        // Accessing Services triggers the test host to build. MigrateAsync
        // creates RequestsManagement_Tests and applies all pending migrations.
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    // Actual resource disposal is handled by the inherited IDisposable.Dispose()
    // that xUnit calls after this method.
    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    // ── Public helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Deletes all rows from both tables in FK dependency order.
    /// Call this in each test class's IAsyncLifetime.InitializeAsync() to
    /// guarantee a clean baseline before that class's tests execute.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Verify the live connection targets the test database before touching any data.
        var actualDb = db.Database.GetDbConnection().Database;
        if (!actualDb.Equals("RequestsManagement_Tests", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"ResetAsync refuses to run against '{actualDb}'. " +
                "The active connection must target 'RequestsManagement_Tests'.");

        // Both DELETEs in one transaction: either both succeed or both roll back.
        await using var tx = await db.Database.BeginTransactionAsync();
        // Child table first to satisfy the FK constraint.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [RequestStatusHistory]");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [Requests]");
        await tx.CommitAsync();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string GetTestConnectionString()
    {
        var cs = Environment.GetEnvironmentVariable("TEST_CONNECTION_STRING")
                 ?? "Server=(local);Database=RequestsManagement_Tests;Integrated Security=True;TrustServerCertificate=True;";

        // SqlConnectionStringBuilder.InitialCatalog is the authoritative way to
        // extract the database name regardless of which keyword variant was used
        // (Database= vs Initial Catalog=).
        var csb = new SqlConnectionStringBuilder(cs);

        if (!csb.InitialCatalog.Equals("RequestsManagement_Tests", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Integration tests must target 'RequestsManagement_Tests'. " +
                $"The connection string points to '{csb.InitialCatalog}'. " +
                "Set the TEST_CONNECTION_STRING environment variable to fix this.");

        return cs;
    }
}
