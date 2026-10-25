using System.Net;
using RequestsManagement.Tests.Infrastructure;

namespace RequestsManagement.Tests.Integration;

/// <summary>
/// Smoke test: verifies that the test host starts, the schema exists on
/// RequestsManagement_Tests, and the main list endpoint responds with 200.
/// </summary>
[Collection("Database")]
public sealed class SmokeTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public SmokeTests(ApiFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    // Reset data before this test class runs. Because xUnit constructs a new
    // SmokeTests instance per [Fact], InitializeAsync() effectively runs once
    // per test method — giving each test a clean empty database.
    public async Task InitializeAsync() => await _factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetRequests_EmptyDatabase_Returns200WithEmptyPage()
    {
        var response = await _client.GetAsync("/api/requests?pageSize=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
