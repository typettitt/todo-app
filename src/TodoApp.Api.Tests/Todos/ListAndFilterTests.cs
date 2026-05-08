using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using TodoApp.Api.Tests.Auth;

namespace TodoApp.Api.Tests.Todos;

[Trait("Category", "Todos")]
public sealed class ListAndFilterTests : IAsyncLifetime, IDisposable
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
    public async Task ListTodos_HasNext_True_WhenMoreRowsExist()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        for (var i = 0; i < 25; i++)
        {
            await TodoTestHelpers.CreateTodoAsync(alice, $"t{i:D2}");
        }

        var page1 = await alice.GetFromJsonAsync<JsonElement>(new Uri("/api/todos?page=1&pageSize=20", UriKind.Relative));
        page1.GetProperty("total").GetInt32().Should().Be(25);
        page1.GetProperty("hasNext").GetBoolean().Should().BeTrue();
        page1.GetProperty("items").GetArrayLength().Should().Be(20);
    }

    [Fact]
    public async Task ListTodos_HasNext_False_OnLastPage()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        for (var i = 0; i < 25; i++)
        {
            await TodoTestHelpers.CreateTodoAsync(alice, $"t{i:D2}");
        }

        var page2 = await alice.GetFromJsonAsync<JsonElement>(new Uri("/api/todos?page=2&pageSize=20", UriKind.Relative));
        page2.GetProperty("hasNext").GetBoolean().Should().BeFalse();
        page2.GetProperty("items").GetArrayLength().Should().Be(5);
    }

    [Fact]
    public async Task ListTodos_All4Tabs_Return_ExpectedRows()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var t1 = await TodoTestHelpers.CreateTodoAsync(alice, "active-no-due");
        var t2 = await TodoTestHelpers.CreateTodoAsync(alice, "active-due-today", dueDate: today);
        var t3 = await TodoTestHelpers.CreateTodoAsync(alice, "to-complete");

        // Complete t3.
        await TodoTestHelpers.PatchRawAsync(alice, $"/api/todos/{t3.Id()}/complete", new { isCompleted = true, rowVersion = t3.RowVersion() });

        var all = await alice.GetFromJsonAsync<JsonElement>(new Uri("/api/todos?status=All", UriKind.Relative));
        all.GetProperty("total").GetInt32().Should().Be(3);

        var active = await alice.GetFromJsonAsync<JsonElement>(new Uri("/api/todos?status=Active", UriKind.Relative));
        active.GetProperty("total").GetInt32().Should().Be(2);

        var completed = await alice.GetFromJsonAsync<JsonElement>(new Uri("/api/todos?status=Completed", UriKind.Relative));
        completed.GetProperty("total").GetInt32().Should().Be(1);

        var due = await alice.GetFromJsonAsync<JsonElement>(new Uri($"/api/todos?status=DueToday&today={today:yyyy-MM-dd}", UriKind.Relative));
        due.GetProperty("total").GetInt32().Should().Be(1);
        due.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(t2.Id());
    }

    [Fact]
    public async Task PageSize_Over100_IsClampedTo100()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var resp = await alice.GetFromJsonAsync<JsonElement>(new Uri("/api/todos?pageSize=10000", UriKind.Relative));
        resp.GetProperty("pageSize").GetInt32().Should().Be(100);
    }

    [Fact]
    public async Task Search_FindsByTitle_AndDescription()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        await TodoTestHelpers.CreateTodoAsync(alice, "Buy groceries");
        await TodoTestHelpers.CreateTodoAsync(alice, "Walk dog", description: "groceries needed");
        await TodoTestHelpers.CreateTodoAsync(alice, "Read book");

        var resp = await alice.GetFromJsonAsync<JsonElement>(new Uri("/api/todos?q=groceries", UriKind.Relative));
        resp.GetProperty("total").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task DueWindow_ReturnsOnlyRowsInsideInclusiveRange()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var before = await TodoTestHelpers.CreateTodoAsync(alice, "Before window", dueDate: new DateOnly(2026, 5, 1));
        var insideA = await TodoTestHelpers.CreateTodoAsync(alice, "Inside A", dueDate: new DateOnly(2026, 5, 10));
        var insideB = await TodoTestHelpers.CreateTodoAsync(alice, "Inside B", dueDate: new DateOnly(2026, 5, 20));
        var after = await TodoTestHelpers.CreateTodoAsync(alice, "After window", dueDate: new DateOnly(2026, 6, 1));
        await TodoTestHelpers.CreateTodoAsync(alice, "Unscheduled");

        var resp = await alice.GetFromJsonAsync<JsonElement>(
            new Uri("/api/todos?dueFrom=2026-05-10&dueTo=2026-05-20&sortBy=DueDate&sortDir=Asc", UriKind.Relative));

        resp.GetProperty("total").GetInt32().Should().Be(2);
        var ids = resp.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray();
        ids.Should().BeEquivalentTo(new[] { insideA.Id(), insideB.Id() });
        ids.Should().NotContain(before.Id());
        ids.Should().NotContain(after.Id());
    }

    [Fact]
    public async Task SortByPriority_UsesDomainOrder_LowMediumHigh()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        await TodoTestHelpers.CreateTodoAsync(alice, "high", priority: "High");
        await TodoTestHelpers.CreateTodoAsync(alice, "low", priority: "Low");
        await TodoTestHelpers.CreateTodoAsync(alice, "medium", priority: "Medium");

        var resp = await alice.GetFromJsonAsync<JsonElement>(
            new Uri("/api/todos?sortBy=Priority&sortDir=Asc", UriKind.Relative));

        var priorities = resp.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("priority").GetString())
            .ToArray();
        priorities.Should().Equal("Low", "Medium", "High");
    }
}
