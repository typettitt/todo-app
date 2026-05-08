using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using TodoApp.Api.Features.Todos;
using TodoApp.Api.Tests.Auth;

namespace TodoApp.Api.Tests.Todos;

/// <summary>
/// Authenticated abuse controls. Each fact owns its own factory so the
/// fixed-window rate-limit buckets do not bleed across tests. The `todos-write`
/// (60/min/sub) and `todos-read` (600/min/sub) policies partition by the JWT
/// `sub` claim — see <see cref="TodoEndpoints.WriteRateLimitPolicy"/> /
/// <see cref="TodoEndpoints.ReadRateLimitPolicy"/>.
/// </summary>
[Trait("Category", "Todos")]
public sealed class AbuseControlsTests : IAsyncLifetime, IDisposable
{
    private TestWebApplicationFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new TestWebApplicationFactory();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => _factory?.Dispose();

    [Fact]
    public async Task TodosWrite_RateLimit_61stRequest_Returns429()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");

        // 60 successful writes inside the 1-minute window.
        for (var i = 0; i < 60; i++)
        {
            using var resp = await TodoTestHelpers.PostRawAsync(alice, "/api/todos", new { title = $"t{i:D2}" });
            resp.StatusCode.Should().Be(HttpStatusCode.Created, "request {0} must succeed before the cap kicks in", i + 1);
        }

        using var blocked = await TodoTestHelpers.PostRawAsync(alice, "/api/todos", new { title = "over-cap" });
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task TodosRead_RateLimit_601stRequest_Returns429()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");

        for (var i = 0; i < 600; i++)
        {
            using var resp = await alice.GetAsync(new Uri("/api/todos", UriKind.Relative));
            resp.StatusCode.Should().Be(HttpStatusCode.OK, "read {0} must succeed before the cap kicks in", i + 1);
        }

        using var blocked = await alice.GetAsync(new Uri("/api/todos", UriKind.Relative));
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task TodosWrite_RateLimit_PartitionsBySub_OneUserDoesNotStarveAnother()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var bob = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "bob@example.com");

        // Alice burns her entire 60/min write budget.
        for (var i = 0; i < 60; i++)
        {
            using var resp = await TodoTestHelpers.PostRawAsync(alice, "/api/todos", new { title = $"a{i:D2}" });
            resp.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using var aliceBlocked = await TodoTestHelpers.PostRawAsync(alice, "/api/todos", new { title = "a-over" });
        aliceBlocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // Bob's bucket is independent — his first request must still succeed.
        using var bobOk = await TodoTestHelpers.PostRawAsync(bob, "/api/todos", new { title = "b-ok" });
        bobOk.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ListTodos_PageOver10000_Returns400()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");

        using var resp = await alice.GetAsync(new Uri("/api/todos?page=10001&pageSize=20", UriKind.Relative));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.GetProperty("errors").GetProperty("page").EnumerateArray().Single().GetString()
            .Should().Contain("10000");
    }

    [Fact]
    public async Task ListTodos_PageAtBoundary10000_IsAccepted()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");

        using var resp = await alice.GetAsync(new Uri("/api/todos?page=10000&pageSize=20", UriKind.Relative));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListTodos_QueryOver100Chars_Returns400()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");

        var oversize = new string('a', 101);
        using var resp = await alice.GetAsync(new Uri($"/api/todos?q={oversize}", UriKind.Relative));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors").GetProperty("q").EnumerateArray().Single().GetString()
            .Should().Contain("100");
    }

    [Fact]
    public async Task ListTodos_QueryAtBoundary100Chars_IsAccepted()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");

        var atLimit = new string('a', 100);
        using var resp = await alice.GetAsync(new Uri($"/api/todos?q={atLimit}", UriKind.Relative));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListTodos_LikeEscape_PercentDoesNotWildcardMatch()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");

        // Three rows: only the literal-percent row contains '%'. A naive
        // unescaped LIKE %{q}% would match all three because '%' inside the
        // query collapses to a wildcard, defeating row-scoping for searches.
        await TodoTestHelpers.CreateTodoAsync(alice, "buy milk");
        await TodoTestHelpers.CreateTodoAsync(alice, "review PR");
        await TodoTestHelpers.CreateTodoAsync(alice, "100% complete");

        using var resp = await alice.GetAsync(new Uri("/api/todos?q=%25", UriKind.Relative));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var titles = body.GetProperty("items").EnumerateArray()
            .Select(t => t.GetProperty("title").GetString())
            .ToList();
        titles.Should().ContainSingle().Which.Should().Be("100% complete");
    }

    [Fact]
    public async Task ListTodos_LikeEscape_UnderscoreDoesNotWildcardMatch()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");

        // Underscore is the single-char wildcard in SQL LIKE. Without escaping,
        // `q=a_c` would match "abc" and "axc". With escaping, it must only
        // match the literal "a_c".
        await TodoTestHelpers.CreateTodoAsync(alice, "abc");
        await TodoTestHelpers.CreateTodoAsync(alice, "axc");
        await TodoTestHelpers.CreateTodoAsync(alice, "a_c literal");

        using var resp = await alice.GetAsync(new Uri("/api/todos?q=a_c", UriKind.Relative));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var titles = body.GetProperty("items").EnumerateArray()
            .Select(t => t.GetProperty("title").GetString())
            .ToList();
        titles.Should().ContainSingle().Which.Should().Be("a_c literal");
    }
}
