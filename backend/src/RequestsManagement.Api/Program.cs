using Microsoft.EntityFrameworkCore;
using RequestsManagement.Api.Data;
using RequestsManagement.Api.Data.Seeding;
using RequestsManagement.Api.Infrastructure;
using RequestsManagement.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Services ────────────────────────────────────────────────────────────────

builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        // Serialize enums as strings in all API responses (e.g. "New" instead of 0)
        opts.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

// Return RFC 7807 ProblemDetails for model-validation errors (HTTP 400).
// This avoids the default plain-text format and keeps the contract consistent.
builder.Services.AddProblemDetails();

// Centralized exception handler — logs and returns safe ProblemDetails (HTTP 500).
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// TimeProvider.System is the real clock; Singleton because it has no state.
// Injected into RequestStatusService so UpdatedAt is always sourced from one place
// and can be replaced with a fake in tests.
builder.Services.AddSingleton(TimeProvider.System);

// Application services — Scoped matches DbContext lifetime (one per HTTP request)
builder.Services.AddScoped<IRequestQueryService, RequestQueryService>();
builder.Services.AddScoped<IRequestStatusService, RequestStatusService>();

// EF Core — SQL Server, scoped lifetime (default for DbContext)
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => sql.EnableRetryOnFailure()));

// ── Pipeline ─────────────────────────────────────────────────────────────────

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Must be registered before routing so it can catch exceptions from any middleware.
app.UseExceptionHandler();

app.UseHttpsRedirection();
app.MapControllers();

// ── Seed mode ────────────────────────────────────────────────────────────────
// Run as:  dotnet run --project src/RequestsManagement.Api -- --seed [count]
// Exits after seeding; does NOT start the web server.
if (args.Contains("--seed"))
{
    int seedCount = 100_000;
    int idx = Array.IndexOf(args, "--seed");
    if (idx + 1 < args.Length && int.TryParse(args[idx + 1], out int parsed))
        seedCount = parsed;

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var seederLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    await DataSeeder.RunAsync(db, seederLogger, seedCount);
    return;
}

app.Run();

// Required by WebApplicationFactory in integration tests
public partial class Program { }
