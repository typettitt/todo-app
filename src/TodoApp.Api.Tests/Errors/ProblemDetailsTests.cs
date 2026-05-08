using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using TodoApp.Api.Features.Common;
using TodoApp.Api.Tests.Auth;
using TodoApp.Api.Tests.Todos;

namespace TodoApp.Api.Tests.Errors;

[Trait("Category", "Errors")]
public sealed class ProblemDetailsTests : IAsyncLifetime, IDisposable
{
    private static readonly HashSet<string> CanonicalKeys = new(StringComparer.Ordinal)
    {
        "type",
        "title",
        "status",
        "detail",
        "instance",
        "traceId",
        "errors",
    };

    private static readonly string[] StackTraceMarkers =
    {
        "phase3-secret-exception-message",
        "InvalidOperationException",
        "StackTrace",
        " at TodoApp.",
    };

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
    public async Task BindingError_FromMalformedJson_HasCanonicalShape()
    {
        var client = await TodoTestHelpers.CreateAuthedClientAsync(
            _factory,
            "malformed-json@example.com");

        using var content = new StringContent("{\"title\":", Encoding.UTF8, "application/json");
        var response = await client.PostAsync(new Uri("/api/todos", UriKind.Relative), content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadProblemAsync(response);
        AssertCanonicalShape(problem, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ValidationFilter_FromFluentValidation_HasCanonicalShape()
    {
        var client = await TodoTestHelpers.CreateAuthedClientAsync(
            _factory,
            "validation-filter@example.com");

        var response = await client.PostAsJsonAsync(
            "/api/todos",
            new { title = string.Empty },
            TodoTestHelpers.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadProblemAsync(response);
        AssertCanonicalShape(problem, HttpStatusCode.BadRequest, requireErrors: true);

        var errors = problem.GetProperty("errors");
        errors.TryGetProperty("title", out var titleErrors).Should().BeTrue();
        titleErrors.EnumerateArray().Should().NotBeEmpty();
        errors.TryGetProperty("Title", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrencyException_From409_HasCanonicalShape_WithRowVersionError()
    {
        var client = await TodoTestHelpers.CreateAuthedClientAsync(
            _factory,
            "concurrency@example.com");
        var todo = await TodoTestHelpers.CreateTodoAsync(client, "v1");

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/todos/{todo.Id()}", UriKind.Relative),
            new
            {
                title = "v2",
                description = (string?)null,
                dueDate = (string?)null,
                priority = "Low",
                tags = Array.Empty<string>(),
                rowVersion = todo.RowVersion() + 99u,
            },
            TodoTestHelpers.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await ReadProblemAsync(response);
        AssertCanonicalShape(problem, HttpStatusCode.Conflict, requireErrors: true);

        var messages = problem
            .GetProperty("errors")
            .GetProperty("rowVersion")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();
        messages.Should().Equal(ProblemDetailsBuilder.ConcurrencyRowVersionMessage);
    }

    [Fact]
    public async Task EveryShape_HasTraceId()
    {
        var authed = await TodoTestHelpers.CreateAuthedClientAsync(
            _factory,
            "trace-shapes@example.com");
        var anon = _factory.CreateClient();
        var todo = await TodoTestHelpers.CreateTodoAsync(authed, "trace");

        var validation = await authed.PostAsJsonAsync(
            "/api/todos",
            new { title = string.Empty },
            TodoTestHelpers.Json);
        var unauthorized = await anon.GetAsync(new Uri("/api/todos", UriKind.Relative));
        var notFound = await anon.GetAsync(new Uri("/api/unknown-route", UriKind.Relative));
        var conflict = await authed.PutAsJsonAsync(
            new Uri($"/api/todos/{todo.Id()}", UriKind.Relative),
            new
            {
                title = "trace",
                description = (string?)null,
                dueDate = (string?)null,
                priority = "Low",
                tags = Array.Empty<string>(),
                rowVersion = todo.RowVersion() + 1u,
            },
            TodoTestHelpers.Json);

        var responses = new[]
        {
            (validation, HttpStatusCode.BadRequest),
            (unauthorized, HttpStatusCode.Unauthorized),
            (notFound, HttpStatusCode.NotFound),
            (conflict, HttpStatusCode.Conflict),
        };

        foreach (var (response, statusCode) in responses)
        {
            response.StatusCode.Should().Be(statusCode);
            var problem = await ReadProblemAsync(response);
            AssertCanonicalShape(problem, statusCode);
            problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task NotFoundReturnsProblemDetails()
    {
        var client = await TodoTestHelpers.CreateAuthedClientAsync(
            _factory,
            "not-found-problem@example.com");

        using var response = await client.GetAsync(new Uri($"/api/todos/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await ReadProblemAsync(response);
        AssertCanonicalShape(problem, HttpStatusCode.NotFound);
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task FallbackHandler_DoesNotLeakStackTrace_InResponseBody()
    {
        using var factory = new TestWebApplicationFactory(enableExceptionProbe: true);
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/__test/throw", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        foreach (var marker in StackTraceMarkers)
        {
            body.Should().NotContain(marker);
        }

        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement.Clone();
        AssertCanonicalShape(problem, HttpStatusCode.InternalServerError);

        var traceId = problem.GetProperty("traceId").GetString();
        traceId.Should().NotBeNullOrWhiteSpace();
        factory.StartupLogLines.Should().Contain(line =>
            line.Contains("Unhandled exception", StringComparison.Ordinal)
            && line.Contains(traceId!, StringComparison.Ordinal));
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.Should().Be(ProblemDetailsBuilder.ContentType);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }

    private static void AssertCanonicalShape(
        JsonElement problem,
        HttpStatusCode statusCode,
        bool requireErrors = false)
    {
        var propertyNames = problem.EnumerateObject().Select(p => p.Name).ToArray();
        propertyNames.Should().OnlyContain(name => CanonicalKeys.Contains(name));
        propertyNames.Should().NotContain("extensions");

        problem.GetProperty("type").GetString().Should().Be(ProblemDetailsBuilder.DefaultType);
        problem.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        problem.GetProperty("status").GetInt32().Should().Be((int)statusCode);
        problem.GetProperty("instance").GetString().Should().NotBeNullOrWhiteSpace();
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();

        if (requireErrors)
        {
            problem.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Object);
        }
    }
}
