using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using TodoApp.Api.Data;
using TodoApp.Api.Tests.Auth;

namespace TodoApp.Api.Tests.Todos;

[Trait("Category", "Ownership")]
public sealed class OwnershipTests : IAsyncLifetime, IDisposable
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

    private async Task<(HttpClient alice, HttpClient bob, Guid bobsTodoId, uint bobsRowVersion)> SeedAsync()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var bob = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "bob@example.com");

        await TodoTestHelpers.CreateTodoAsync(alice, "Alice 1");
        await TodoTestHelpers.CreateTodoAsync(alice, "Alice 2");
        var bobTodo = await TodoTestHelpers.CreateTodoAsync(bob, "Bob 1");

        return (alice, bob, bobTodo.Id(), bobTodo.RowVersion());
    }

    [Fact]
    public async Task Alice_GetTodos_DoesNotIncludeBobsTodos()
    {
        var (alice, _, bobsTodoId, _) = await SeedAsync();

        var resp = await alice.GetAsync(new Uri("/api/todos", UriKind.Relative));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(2);
        items.Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(bobsTodoId);
        // Defense-in-depth: response payload must not leak userId.
        foreach (var item in items)
        {
            item.TryGetProperty("userId", out _).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Alice_GetTodoById_BobsTodo_Returns404_NotForbidden()
    {
        var (alice, _, bobsTodoId, _) = await SeedAsync();

        var resp = await alice.GetAsync(new Uri($"/api/todos/{bobsTodoId}", UriKind.Relative));
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Alice_PutTodo_BobsTodo_Returns404()
    {
        var (alice, _, bobsTodoId, bobsRv) = await SeedAsync();

        var payload = new
        {
            title = "hijack",
            description = (string?)null,
            dueDate = (string?)null,
            priority = "Low",
            tags = Array.Empty<string>(),
            rowVersion = bobsRv,
        };
        var resp = await alice.PutAsJsonAsync(new Uri($"/api/todos/{bobsTodoId}", UriKind.Relative), payload, TodoTestHelpers.Json);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Alice_PatchComplete_BobsTodo_Returns404()
    {
        var (alice, _, bobsTodoId, bobsRv) = await SeedAsync();

        var payload = new { isCompleted = true, rowVersion = bobsRv };
        var resp = await TodoTestHelpers.PatchRawAsync(alice, $"/api/todos/{bobsTodoId}/complete", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Alice_DeleteTodo_BobsTodo_Returns404_AndBobsTodoStillExists()
    {
        var (alice, _, bobsTodoId, _) = await SeedAsync();

        var resp = await alice.DeleteAsync(new Uri($"/api/todos/{bobsTodoId}", UriKind.Relative));
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Verify Bob's todo persists by direct DB inspection (using IgnoreQueryFilters
        // is permitted in tests; production code is grep-banned).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        var stillThere = await db.Todos.IgnoreQueryFilters().AnyAsync(t => t.Id == bobsTodoId);
        stillThere.Should().BeTrue();
    }

    [Fact]
    public async Task Alice_PutWithUserIdField_DoesNotChangeOwner()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var bob = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "bob@example.com");

        var aliceTodo = await TodoTestHelpers.CreateTodoAsync(alice, "Alice's");

        // Resolve Bob's UserId via DB.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        var bobUserId = await db.Users.AsNoTracking()
            .Where(u => u.Email == "bob@example.com")
            .Select(u => u.Id)
            .SingleAsync();

        var body = new JsonObject
        {
            ["title"] = "Updated",
            ["description"] = null,
            ["dueDate"] = null,
            ["priority"] = "Low",
            ["tags"] = new JsonArray(),
            ["rowVersion"] = aliceTodo.RowVersion(),
            ["userId"] = bobUserId.ToString(),
        };

        var resp = await TodoTestHelpers.PutRawAsync(alice, $"/api/todos/{aliceTodo.Id()}", body);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await db.Todos.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(t => t.Id == aliceTodo.Id());
        stored.UserId.Should().NotBe(bobUserId, "PUT must never change ownership");
    }

    [Fact]
    public async Task Alice_DueToday_DoesNotIncludeBobsTodos()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var bob = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "bob@example.com");
        var today = new DateOnly(2026, 5, 7);

        await TodoTestHelpers.CreateTodoAsync(alice, "A1", dueDate: today);
        await TodoTestHelpers.CreateTodoAsync(alice, "A2", dueDate: today.AddDays(1));
        await TodoTestHelpers.CreateTodoAsync(bob, "B1", dueDate: today);
        await TodoTestHelpers.CreateTodoAsync(bob, "B2", dueDate: today);

        var aliceList = await alice.GetFromJsonAsync<JsonElement>(
            new Uri("/api/todos?status=DueToday&today=2026-05-07", UriKind.Relative));
        aliceList.GetProperty("total").GetInt32().Should().Be(1);
        aliceList.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("title").GetString())
            .Should().BeEquivalentTo(new[] { "A1" });
    }
}
