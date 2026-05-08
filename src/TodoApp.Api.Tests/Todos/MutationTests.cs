using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using TodoApp.Api.Features.Common;
using TodoApp.Api.Tests.Auth;

namespace TodoApp.Api.Tests.Todos;

[Trait("Category", "Todos")]
public sealed class MutationTests : IAsyncLifetime, IDisposable
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
    public async Task Put_FreshRowVersion_ReturnsBody_WithIncrementedRowVersion()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var initial = await TodoTestHelpers.CreateTodoAsync(alice, "v1");

        var resp = await alice.PutAsJsonAsync(
            new Uri($"/api/todos/{initial.Id()}", UriKind.Relative),
            new
            {
                title = "v2",
                description = (string?)null,
                dueDate = (string?)null,
                priority = "Low",
                tags = Array.Empty<string>(),
                rowVersion = initial.RowVersion(),
            },
            TodoTestHelpers.Json);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("title").GetString().Should().Be("v2");
        body.RowVersion().Should().Be(initial.RowVersion() + 1u);
    }

    [Fact]
    public async Task Put_StaleRowVersion_Returns409()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var initial = await TodoTestHelpers.CreateTodoAsync(alice, "v1");

        var resp = await alice.PutAsJsonAsync(
            new Uri($"/api/todos/{initial.Id()}", UriKind.Relative),
            new
            {
                title = "v2",
                description = (string?)null,
                dueDate = (string?)null,
                priority = "Low",
                tags = Array.Empty<string>(),
                rowVersion = initial.RowVersion() + 99u,
            },
            TodoTestHelpers.Json);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var pd = await resp.Content.ReadFromJsonAsync<JsonElement>();
        pd.GetProperty("errors").GetProperty("rowVersion").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Put_MissingPriority_Returns400()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var initial = await TodoTestHelpers.CreateTodoAsync(alice, "v1");

        var resp = await alice.PutAsJsonAsync(
            new Uri($"/api/todos/{initial.Id()}", UriKind.Relative),
            new
            {
                title = "v2",
                description = (string?)null,
                dueDate = (string?)null,
                tags = Array.Empty<string>(),
                rowVersion = initial.RowVersion(),
            },
            TodoTestHelpers.Json);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var pd = await resp.Content.ReadFromJsonAsync<JsonElement>();
        pd.GetProperty("errors").GetProperty("priority").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Put_MissingRowVersion_Returns400()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var initial = await TodoTestHelpers.CreateTodoAsync(alice, "v1");

        var resp = await alice.PutAsJsonAsync(
            new Uri($"/api/todos/{initial.Id()}", UriKind.Relative),
            new
            {
                title = "v2",
                description = (string?)null,
                dueDate = (string?)null,
                priority = "Low",
                tags = Array.Empty<string>(),
            },
            TodoTestHelpers.Json);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var pd = await resp.Content.ReadFromJsonAsync<JsonElement>();
        pd.GetProperty("errors").GetProperty("rowVersion").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task PatchComplete_FreshRowVersion_ReturnsBody_WithIncrementedRowVersion()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var initial = await TodoTestHelpers.CreateTodoAsync(alice, "complete-me");

        var resp = await TodoTestHelpers.PatchRawAsync(
            alice,
            $"/api/todos/{initial.Id()}/complete",
            new { isCompleted = true, rowVersion = initial.RowVersion() });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isCompleted").GetBoolean().Should().BeTrue();
        body.RowVersion().Should().Be(initial.RowVersion() + 1u);
    }

    [Fact]
    public async Task PatchComplete_WhenCompleting_SetsCompletedAt()
    {
        var completedAt = new DateTimeOffset(2026, 5, 8, 10, 15, 0, TimeSpan.Zero);
        await using var factory = new TestWebApplicationFactory(configureServices: services =>
        {
            var clock = services.Single(d => d.ServiceType == typeof(IClock));
            services.Remove(clock);
            services.AddSingleton<IClock>(new FixedClock(completedAt));
        });
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(factory, "alice@example.com");
        var initial = await TodoTestHelpers.CreateTodoAsync(alice, "complete-me");

        var resp = await TodoTestHelpers.PatchRawAsync(
            alice,
            $"/api/todos/{initial.Id()}/complete",
            new { isCompleted = true, rowVersion = initial.RowVersion() });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("completedAt").GetDateTimeOffset().Should().Be(completedAt);
    }

    [Fact]
    public async Task PatchComplete_WhenReopening_ClearsCompletedAt()
    {
        var completedAt = new DateTimeOffset(2026, 5, 8, 10, 15, 0, TimeSpan.Zero);
        await using var factory = new TestWebApplicationFactory(configureServices: services =>
        {
            var clock = services.Single(d => d.ServiceType == typeof(IClock));
            services.Remove(clock);
            services.AddSingleton<IClock>(new FixedClock(completedAt));
        });
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(factory, "alice@example.com");
        var initial = await TodoTestHelpers.CreateTodoAsync(alice, "complete-me");
        var complete = await TodoTestHelpers.PatchRawAsync(
            alice,
            $"/api/todos/{initial.Id()}/complete",
            new { isCompleted = true, rowVersion = initial.RowVersion() });
        var completed = await complete.Content.ReadFromJsonAsync<JsonElement>();

        var reopen = await TodoTestHelpers.PatchRawAsync(
            alice,
            $"/api/todos/{initial.Id()}/complete",
            new { isCompleted = false, rowVersion = completed.RowVersion() });

        reopen.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await reopen.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isCompleted").GetBoolean().Should().BeFalse();
        body.GetProperty("completedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task PatchComplete_StaleRowVersion_Returns409()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var initial = await TodoTestHelpers.CreateTodoAsync(alice, "complete-me");

        var resp = await TodoTestHelpers.PatchRawAsync(
            alice,
            $"/api/todos/{initial.Id()}/complete",
            new { isCompleted = true, rowVersion = initial.RowVersion() + 99u });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PatchComplete_MissingIsCompleted_Returns400()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var initial = await TodoTestHelpers.CreateTodoAsync(alice, "complete-me");

        var resp = await TodoTestHelpers.PatchRawAsync(
            alice,
            $"/api/todos/{initial.Id()}/complete",
            new { rowVersion = initial.RowVersion() });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var pd = await resp.Content.ReadFromJsonAsync<JsonElement>();
        pd.GetProperty("errors").GetProperty("isCompleted").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task PatchComplete_MissingRowVersion_Returns400()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var initial = await TodoTestHelpers.CreateTodoAsync(alice, "complete-me");

        var resp = await TodoTestHelpers.PatchRawAsync(
            alice,
            $"/api/todos/{initial.Id()}/complete",
            new { isCompleted = true });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var pd = await resp.Content.ReadFromJsonAsync<JsonElement>();
        pd.GetProperty("errors").GetProperty("rowVersion").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Put_OmitsDescription_ClearsDescription()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var initial = await TodoTestHelpers.CreateTodoAsync(alice, "t1", description: "preset");
        initial.GetProperty("description").GetString().Should().Be("preset");

        var resp = await alice.PutAsJsonAsync(
            new Uri($"/api/todos/{initial.Id()}", UriKind.Relative),
            new
            {
                title = "t1",
                // description omitted entirely → null after binding → cleared
                dueDate = (string?)null,
                priority = "Low",
                tags = Array.Empty<string>(),
                rowVersion = initial.RowVersion(),
            },
            TodoTestHelpers.Json);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var desc = body.GetProperty("description");
        desc.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Put_OmitsTags_ClearsTags()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var initial = await TodoTestHelpers.CreateTodoAsync(alice, "t1", tags: new[] { "a", "b" });
        initial.GetProperty("tags").GetArrayLength().Should().Be(2);

        var resp = await alice.PutAsJsonAsync(
            new Uri($"/api/todos/{initial.Id()}", UriKind.Relative),
            new
            {
                title = "t1",
                description = (string?)null,
                dueDate = (string?)null,
                priority = "Low",
                tags = Array.Empty<string>(),
                rowVersion = initial.RowVersion(),
            },
            TodoTestHelpers.Json);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("tags").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Delete_RemovesRow()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var todo = await TodoTestHelpers.CreateTodoAsync(alice, "x");

        var del = await alice.DeleteAsync(new Uri($"/api/todos/{todo.Id()}", UriKind.Relative));
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var followup = await alice.GetAsync(new Uri($"/api/todos/{todo.Id()}", UriKind.Relative));
        followup.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; } = now;
    }
}
