using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using TodoApp.Api.Tests.Auth;

namespace TodoApp.Api.Tests.Todos;

[Trait("Category", "Todos")]
public sealed class TagsAndDateTests : IAsyncLifetime, IDisposable
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
    public async Task Tags_RoundTrip_PreservesOrder()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var todo = await TodoTestHelpers.CreateTodoAsync(alice, "x", tags: new[] { "z", "a", "m" });

        var fetched = await alice.GetFromJsonAsync<JsonElement>(new Uri($"/api/todos/{todo.Id()}", UriKind.Relative));
        fetched.GetProperty("tags").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("z", "a", "m");
    }

    [Fact]
    public async Task Tags_RoundTrip_PreservesUnicode()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var todo = await TodoTestHelpers.CreateTodoAsync(alice, "x", tags: new[] { "こんにちは", "🚀", "café" });

        var fetched = await alice.GetFromJsonAsync<JsonElement>(new Uri($"/api/todos/{todo.Id()}", UriKind.Relative));
        fetched.GetProperty("tags").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("こんにちは", "🚀", "café");
    }

    [Fact]
    public async Task Tags_RoundTrip_EmptyArray()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var todo = await TodoTestHelpers.CreateTodoAsync(alice, "x", tags: Array.Empty<string>());
        todo.GetProperty("tags").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Tags_Update_EmptyArray_ClearsExistingTags()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var todo = await TodoTestHelpers.CreateTodoAsync(alice, "x", tags: new[] { "a", "b" });

        var resp = await alice.PutAsJsonAsync(
            new Uri($"/api/todos/{todo.Id()}", UriKind.Relative),
            new
            {
                title = "x",
                description = (string?)null,
                dueDate = (string?)null,
                priority = "Low",
                tags = Array.Empty<string>(),
                rowVersion = todo.RowVersion(),
            },
            TodoTestHelpers.Json);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("tags").GetArrayLength().Should().Be(0);

        // Reload via GET to confirm it's persisted, not just echoed.
        var fetched = await alice.GetFromJsonAsync<JsonElement>(new Uri($"/api/todos/{todo.Id()}", UriKind.Relative));
        fetched.GetProperty("tags").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task DueDate_StoredAsIsoText_RoundTrips_YYYYMMDD()
    {
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var todo = await TodoTestHelpers.CreateTodoAsync(alice, "x", dueDate: new DateOnly(2026, 12, 31));

        // Bypass EF and read the raw column value to confirm it's TEXT and YYYY-MM-DD.
        // SQLite stores Guid via EF as upper-case 8-4-4-4-12 text; match that.
        await using var cmd = _factory.Connection.CreateCommand();
        cmd.CommandText = "SELECT DueDate FROM Todos WHERE Id = $id";
        var p = cmd.CreateParameter();
        p.ParameterName = "$id";
        p.Value = todo.Id().ToString().ToUpperInvariant();
        cmd.Parameters.Add(p);

        var raw = await cmd.ExecuteScalarAsync();
        raw.Should().Be("2026-12-31");

        // Also verify the DueDate column type is TEXT.
        await using var pragma = _factory.Connection.CreateCommand();
        pragma.CommandText = "SELECT type FROM pragma_table_info('Todos') WHERE name = 'DueDate'";
        var colType = await pragma.ExecuteScalarAsync();
        colType.Should().Be("TEXT");
    }

    [Fact]
    public async Task DueToday_TokyoFencepost_TrustsClientTodayParam()
    {
        // Tokyo just past midnight: client's local date is 2026-05-08 even though UTC is 2026-05-07.
        // The API trusts the client `today` param — server never computes it.
        var alice = await TodoTestHelpers.CreateAuthedClientAsync(_factory, "alice@example.com");
        var t8 = await TodoTestHelpers.CreateTodoAsync(alice, "tokyo-today", dueDate: new DateOnly(2026, 5, 8));
        var t7 = await TodoTestHelpers.CreateTodoAsync(alice, "utc-today", dueDate: new DateOnly(2026, 5, 7));

        var resp = await alice.GetFromJsonAsync<JsonElement>(new Uri("/api/todos?status=DueToday&today=2026-05-08", UriKind.Relative));
        resp.GetProperty("total").GetInt32().Should().Be(1);
        resp.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(t8.Id());

        // Confirm 2026-05-07 is excluded.
        var resp2 = await alice.GetFromJsonAsync<JsonElement>(new Uri("/api/todos?status=DueToday&today=2026-05-08", UriKind.Relative));
        resp2.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())
            .Should().NotContain(t7.Id());
    }
}
