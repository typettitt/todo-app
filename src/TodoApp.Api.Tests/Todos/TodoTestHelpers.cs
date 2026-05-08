using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using FluentAssertions;

using TodoApp.Api.Tests.Auth;

namespace TodoApp.Api.Tests.Todos;

internal static class TodoTestHelpers
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>
    /// Registers a fresh user against the supplied factory and returns a client with
    /// the cookie jar set to that user's auth cookie. Each call uses a unique HTTP
    /// client (no cookie sharing) so callers can stand up Alice/Bob in isolation.
    /// </summary>
    public static async Task<HttpClient> CreateAuthedClientAsync(
        TestWebApplicationFactory factory,
        string email,
        string password = "Password1!")
    {
        ArgumentNullException.ThrowIfNull(factory);

        var client = factory.CreateClient();
        var register = await client.RegisterAsync(email, password);
        // Register always returns 200. Cookie presence tells us whether the
        // new-user branch ran. If absent, the email is already registered, so
        // fall back to login to obtain a fresh cookie.
        register.StatusCode.Should().Be(HttpStatusCode.OK);
        if (register.GetAuthCookieValue() is null)
        {
            var login = await client.LoginAsync(email, password);
            login.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        return client;
    }

    public static async Task<JsonElement> CreateTodoAsync(
        HttpClient client,
        string title,
        string? description = null,
        DateOnly? dueDate = null,
        string? priority = null,
        string[]? tags = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        var payload = new
        {
            title,
            description,
            dueDate = dueDate?.ToString("yyyy-MM-dd"),
            priority,
            tags = tags ?? Array.Empty<string>(),
        };

        var resp = await client.PostAsJsonAsync("/api/todos", payload, Json);
        resp.StatusCode.Should().Be(HttpStatusCode.Created, "create call must succeed; body: {0}", await resp.Content.ReadAsStringAsync());
        return await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    public static async Task<HttpResponseMessage> PostRawAsync(HttpClient client, string url, object body)
    {
        ArgumentNullException.ThrowIfNull(client);
        var json = JsonSerializer.Serialize(body, Json);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync(new Uri(url, UriKind.Relative), content);
    }

    public static async Task<HttpResponseMessage> PutRawAsync(HttpClient client, string url, JsonNode body)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(body);
        using var content = new StringContent(body.ToJsonString(Json), Encoding.UTF8, "application/json");
        return await client.PutAsync(new Uri(url, UriKind.Relative), content);
    }

    public static async Task<HttpResponseMessage> PatchRawAsync(HttpClient client, string url, object body)
    {
        ArgumentNullException.ThrowIfNull(client);
        var json = JsonSerializer.Serialize(body, Json);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PatchAsync(new Uri(url, UriKind.Relative), content);
    }

    public static Guid Id(this JsonElement todo) => todo.GetProperty("id").GetGuid();

    public static uint RowVersion(this JsonElement todo) => todo.GetProperty("rowVersion").GetUInt32();
}
