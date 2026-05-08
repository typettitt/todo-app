using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using TodoApp.Api.Tests.Auth;

namespace TodoApp.Api.Tests.Todos;

[Trait("Category", "Todos")]
public sealed class ContractTests : IAsyncLifetime, IDisposable
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
    public async Task Post_Returns201_WithLocation_ThatResolves()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");

        var resp = await alice.PostAsJsonAsync("/api/todos", new
        {
            title = "Walk dog",
            description = "Around the block",
            dueDate = "2026-12-31",
            priority = "Medium",
            tags = new[] { "pets" },
        }, TodoTestHelpers.Json);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var location = resp.Headers.Location;
        location.Should().NotBeNull();
        location!.ToString().Should().StartWith("/api/todos/");

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("title").GetString().Should().Be("Walk dog");
        body.TryGetProperty("userId", out _).Should().BeFalse("response must not leak userId");

        // Resolve Location.
        var followup = await alice.GetAsync(location);
        followup.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Post_ResponseBody_IncludesRowVersion_StartingAt1()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var todo = await TodoTestHelpers.CreateTodoAsync(alice, "first");
        todo.RowVersion().Should().Be(1u);
    }

    [Fact]
    public async Task Post_IgnoresClientSuppliedId_CreatedAt_UpdatedAt()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");

        var clientGuid = Guid.NewGuid();
        var resp = await alice.PostAsJsonAsync("/api/todos", new
        {
            id = clientGuid,
            createdAt = "2000-01-01T00:00:00Z",
            updatedAt = "2000-01-01T00:00:00Z",
            title = "x",
            priority = "Low",
            tags = Array.Empty<string>(),
        }, TodoTestHelpers.Json);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().NotBe(clientGuid);
        var created = body.GetProperty("createdAt").GetDateTimeOffset();
        created.Should().BeAfter(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task TodoResponse_HasNoUserId()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var todo = await TodoTestHelpers.CreateTodoAsync(alice, "x");
        todo.TryGetProperty("userId", out _).Should().BeFalse();

        var single = await alice.GetFromJsonAsync<JsonElement>(new Uri($"/api/todos/{todo.Id()}", UriKind.Relative));
        single.TryGetProperty("userId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task DueDate_RoundTrips_AsYYYYMMDD()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var resp = await alice.PostAsJsonAsync("/api/todos", new
        {
            title = "due",
            dueDate = "2026-12-31",
            priority = "Low",
            tags = Array.Empty<string>(),
        }, TodoTestHelpers.Json);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("dueDate").GetString().Should().Be("2026-12-31");
    }

    [Fact]
    public async Task Endpoints_Without_Cookie_Return_401()
    {
        var anon = _factory.CreateClient();

        (await anon.GetAsync(new Uri("/api/todos", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.GetAsync(new Uri($"/api/todos/{Guid.NewGuid()}", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PostAsJsonAsync("/api/todos", new { title = "x" }, TodoTestHelpers.Json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var put = await anon.PutAsJsonAsync(
            new Uri($"/api/todos/{Guid.NewGuid()}", UriKind.Relative),
            new { title = "x", priority = "Low", tags = Array.Empty<string>(), rowVersion = 1u },
            TodoTestHelpers.Json);
        put.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var patch = await TodoTestHelpers.PatchRawAsync(anon, $"/api/todos/{Guid.NewGuid()}/complete", new { isCompleted = true, rowVersion = 1u });
        patch.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await anon.DeleteAsync(new Uri($"/api/todos/{Guid.NewGuid()}", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
