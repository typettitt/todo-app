using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace TodoApp.Api.Tests.Auth;

internal static class TestHelpers
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<HttpResponseMessage> RegisterAsync(
        this HttpClient client,
        string email,
        string password,
        object? extraFields = null)
    {
        if (extraFields is not null)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { email, password }));
            var root = doc.RootElement;
            using var stream = new MemoryStream();
            await using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            foreach (var prop in root.EnumerateObject())
            {
                prop.WriteTo(writer);
            }

            using var extraDoc = JsonDocument.Parse(JsonSerializer.Serialize(extraFields));
            foreach (var prop in extraDoc.RootElement.EnumerateObject())
            {
                prop.WriteTo(writer);
            }

            writer.WriteEndObject();
            await writer.FlushAsync();

            var content = new ByteArrayContent(stream.ToArray());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return await client.PostAsync(new Uri("/api/auth/register", UriKind.Relative), content);
        }

        return await client.PostAsJsonAsync("/api/auth/register", new { email, password }, Json);
    }

    public static async Task<HttpResponseMessage> LoginAsync(
        this HttpClient client,
        string email,
        string password)
        => await client.PostAsJsonAsync("/api/auth/login", new { email, password }, Json);

    public static IEnumerable<string> GetSetCookieHeaders(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values
            : Enumerable.Empty<string>();
    }

    public static string? GetAuthCookieValue(this HttpResponseMessage response, string cookieName = "auth")
    {
        var setCookie = response.GetSetCookieHeaders()
            .FirstOrDefault(h => h.StartsWith(cookieName + "=", StringComparison.Ordinal));
        if (setCookie is null)
        {
            return null;
        }

        var equals = setCookie.IndexOf('=', StringComparison.Ordinal);
        var semi = setCookie.IndexOf(';', StringComparison.Ordinal);
        if (equals < 0)
        {
            return null;
        }

        return semi < 0
            ? setCookie[(equals + 1)..]
            : setCookie[(equals + 1)..semi];
    }

    public static Dictionary<string, string?> ParseCookieAttributes(string setCookieHeader)
    {
        ArgumentNullException.ThrowIfNull(setCookieHeader);
        var parts = setCookieHeader.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (parts.Length == 0)
        {
            return dict;
        }

        var first = parts[0];
        var firstEq = first.IndexOf('=', StringComparison.Ordinal);
        if (firstEq > 0)
        {
            dict["Name"] = first[..firstEq];
            dict["Value"] = first[(firstEq + 1)..];
        }

        foreach (var part in parts.Skip(1))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                dict[part] = null;
            }
            else
            {
                dict[part[..eq]] = part[(eq + 1)..];
            }
        }

        return dict;
    }
}
