using System.Net;

using FluentAssertions;

using TodoApp.Api.Tests.Auth;

namespace TodoApp.Api.Tests.Todos;

[Trait("Category", "Todos")]
public sealed class EnumBindingTests : IAsyncLifetime, IDisposable
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
    public async Task EnumBinding_UnknownStatus_Returns400()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var resp = await alice.GetAsync(new Uri("/api/todos?status=Bogus", UriKind.Relative));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EnumBinding_UnknownSortBy_Returns400()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var resp = await alice.GetAsync(new Uri("/api/todos?sortBy=Bogus", UriKind.Relative));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EnumBinding_UnknownSortDir_Returns400()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var resp = await alice.GetAsync(new Uri("/api/todos?sortDir=Sideways", UriKind.Relative));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EnumBinding_NumericStatus_Returns400()
    {
        // Default minimal-API enum binding for query params still parses the underlying
        // int. We test the JSON converter restriction via a body-bound endpoint below;
        // for query-string Status, this asserts our policy: numeric values should be
        // rejected. If the framework binder still accepts them, this test will fail
        // and we add a `BindAsync` shim.
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var resp = await alice.GetAsync(new Uri("/api/todos?status=2", UriKind.Relative));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DueToday_RequiresTodayParam_Returns400_WhenMissing()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var resp = await alice.GetAsync(new Uri("/api/todos?status=DueToday", UriKind.Relative));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("today");
    }

    [Fact]
    public async Task DueToday_MalformedToday_Returns400()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");

        foreach (var bad in new[] { "not-a-date", "2026-13-99", "2026-05-07T00:00:00Z" })
        {
            var resp = await alice.GetAsync(new Uri($"/api/todos?status=DueToday&today={Uri.EscapeDataString(bad)}", UriKind.Relative));
            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, "bad date input was '{0}'", bad);
        }
    }
}
