using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

using TodoApp.Api.Data;
using TodoApp.Api.Data.Entities;
using TodoApp.Api.Features.Auth;
using TodoApp.Api.Features.Common;
using TodoApp.Api.Tests.Auth;
using TodoApp.Api.Tests.Todos;

namespace TodoApp.Api.Tests.Operational;

[Trait("Category", "Operational")]
public sealed class OperationalTests
{
    private const string ContentTypeOptions = "X-Content-Type-Options";
    private const string FrameOptions = "X-Frame-Options";
    private const string ReferrerPolicy = "Referrer-Policy";
    private const string ContentSecurityPolicy = "Content-Security-Policy";
    private const string PermissionsPolicy = "Permissions-Policy";

    [Fact]
    public async Task OpenApiDocument_IsValid()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        document.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.");
    }

    [Fact]
    public async Task OpenApiDocument_DocumentsTodoResponseBodies_AndDelete204()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var paths = document.RootElement.GetProperty("paths");

        paths.GetProperty("/api/todos").GetProperty("get").GetProperty("responses")
            .GetProperty("200").GetProperty("content").GetProperty("application/json")
            .ValueKind.Should().Be(JsonValueKind.Object);
        paths.GetProperty("/api/todos/{id}").GetProperty("get").GetProperty("responses")
            .GetProperty("200").GetProperty("content").GetProperty("application/json")
            .ValueKind.Should().Be(JsonValueKind.Object);
        paths.GetProperty("/api/todos/{id}").GetProperty("put").GetProperty("responses")
            .GetProperty("200").GetProperty("content").GetProperty("application/json")
            .ValueKind.Should().Be(JsonValueKind.Object);
        paths.GetProperty("/api/todos/{id}/complete").GetProperty("patch").GetProperty("responses")
            .GetProperty("200").GetProperty("content").GetProperty("application/json")
            .ValueKind.Should().Be(JsonValueKind.Object);

        var deleteResponses = paths.GetProperty("/api/todos/{id}")
            .GetProperty("delete")
            .GetProperty("responses");
        deleteResponses.TryGetProperty("204", out _).Should().BeTrue();
        deleteResponses.TryGetProperty("200", out _).Should().BeFalse();
    }

    [Fact]
    public async Task HealthLive_Returns200_WhenAppIsUp()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_Returns200_WhenDbUp()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_Returns503_WhenDbDown()
    {
        await using var factory = new TestWebApplicationFactory(configureServices: services =>
        {
            services.AddHealthChecks()
                .AddCheck<FailingHealthCheck>("forced-ready-failure");
        });
        var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Startup_EnablesSqliteForeignKeyEnforcement()
    {
        await using var factory = new TestWebApplicationFactory();
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys;";
            var result = await command.ExecuteScalarAsync();
            result?.ToString().Should().Be("1");
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    [Fact]
    public async Task HealthReadyRequiresInternalAuth_InNonDevelopment()
    {
        const string healthSecret = "ready-probe-secret";
        var signingKey = Convert.ToBase64String(Enumerable.Repeat((byte)42, 32).ToArray());
        await using var factory = new TestWebApplicationFactory(
            environmentName: "Production",
            signingKey: signingKey,
            configuration: new Dictionary<string, string?>
            {
                [InternalHealthAuth.ConfigKey] = healthSecret,
            });
        var client = factory.CreateClient();

        using var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        using var missing = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        using var wrongRequest = new HttpRequestMessage(HttpMethod.Get, new Uri("/health/ready", UriKind.Relative));
        wrongRequest.Headers.Add(InternalHealthAuth.HeaderName, "wrong-secret");
        using var wrong = await client.SendAsync(wrongRequest);

        using var authorizedRequest = new HttpRequestMessage(HttpMethod.Get, new Uri("/health/ready", UriKind.Relative));
        authorizedRequest.Headers.Add(InternalHealthAuth.HeaderName, healthSecret);
        using var authorized = await client.SendAsync(authorizedRequest);

        live.StatusCode.Should().Be(HttpStatusCode.OK, "liveness must remain public for container health checks");
        missing.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        authorized.StatusCode.Should().Be(HttpStatusCode.OK);

        var problem = await missing.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        problem.GetProperty("status").GetInt32().Should().Be((int)HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_LogsDoNotContainPassword()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();
        var register = await client.RegisterAsync("redaction@example.com", "Password1!");
        register.StatusCode.Should().Be(HttpStatusCode.OK);
        ClearLogs(factory);

        const string secret = "secret-correct-horse";
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "redaction@example.com", password = secret },
            TestHelpers.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        AllLogBytes(factory).Should().NotContain(secret);
    }

    [Fact]
    public async Task Log_TraceId_MatchesProblemDetailsTraceId()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = await TodoTestHelpers.CreateAuthedClientAsync(factory, "trace-id@example.com");
        ClearLogs(factory);

        using var response = await client.GetAsync(new Uri("/api/todos?status=2", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var traceId = problem.GetProperty("traceId").GetString();
        traceId.Should().NotBeNullOrWhiteSpace();
        factory.LogRecords.Should().Contain(record =>
            PropertyEquals(record, "TraceId", traceId!));
    }

    [Fact]
    public async Task Mutation_LogsContain_UserId_And_TodoId_NotBody()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = await TodoTestHelpers.CreateAuthedClientAsync(factory, "audit@example.com");
        using var me = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative));
        var meBody = await me.Content.ReadFromJsonAsync<JsonElement>();
        var userId = meBody.GetProperty("id").GetGuid();

        ClearLogs(factory);
        var marker = "p4-" + Guid.NewGuid().ToString("N");
        using var create = await client.PostAsJsonAsync(
            "/api/todos",
            new
            {
                title = marker,
                description = marker + "-description",
                tags = new[] { marker + "-tag" },
            },
            TodoTestHelpers.Json);

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var todo = await create.Content.ReadFromJsonAsync<JsonElement>();
        var todoId = todo.Id();
        var audit = factory.LogRecords.Should().ContainSingle(record =>
            PropertyEquals(record, "Operation", "Create")
            && PropertyEquals(record, "UserId", userId.ToString())
            && PropertyEquals(record, "TodoId", todoId.ToString())).Subject;
        PropertyEquals(audit, "UserId", userId.ToString()).Should().BeTrue();
        PropertyEquals(audit, "TodoId", todoId.ToString()).Should().BeTrue();
        AllLogBytes(factory).Should().NotContain(marker);
    }

    [Fact]
    public async Task SecurityHeaders_PresentOnEveryResponse()
    {
        await using var factory = new TestWebApplicationFactory(enableExceptionProbe: true);
        var authed = await TodoTestHelpers.CreateAuthedClientAsync(factory, "headers@example.com");
        var anon = factory.CreateClient();

        using var ok = await authed.GetAsync(new Uri("/api/auth/me", UriKind.Relative));
        using var unauthorized = await anon.GetAsync(new Uri("/api/auth/me", UriKind.Relative));
        using var error = await anon.GetAsync(new Uri("/__test/throw", UriKind.Relative));

        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        error.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        AssertSecurityHeaders(ok);
        AssertSecurityHeaders(unauthorized);
        AssertSecurityHeaders(error);
    }

    [Fact]
    public async Task DbInitializer_InProductionEnv_DoesNotSeedDemoUser()
    {
        var priorKey = Environment.GetEnvironmentVariable(JwtKeyProvider.EnvVarName);
        var priorKeyFile = Environment.GetEnvironmentVariable(JwtKeyProvider.EnvFileVarName);
        try
        {
            Environment.SetEnvironmentVariable(
                JwtKeyProvider.EnvVarName,
                Convert.ToBase64String(Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray()));
            Environment.SetEnvironmentVariable(JwtKeyProvider.EnvFileVarName, null);

            await using var factory = new TestWebApplicationFactory(environmentName: "Production");
            _ = factory.CreateClient();

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
            var users = await db.Users.CountAsync();

            users.Should().Be(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable(JwtKeyProvider.EnvVarName, priorKey);
            Environment.SetEnvironmentVariable(JwtKeyProvider.EnvFileVarName, priorKeyFile);
        }
    }

    [Fact]
    public async Task DbInitializer_InDevEnv_Seeds_DemoUser_Idempotently()
    {
        var fixedNow = new DateTimeOffset(2026, 5, 7, 9, 30, 0, TimeSpan.FromHours(-5));
        await using var factory = new TestWebApplicationFactory(enableDemoSeed: true, configureServices: services =>
        {
            var clock = services.Single(d => d.ServiceType == typeof(IClock));
            services.Remove(clock);
            services.AddSingleton<IClock>(new FixedClock(fixedNow));
        });
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        await DbInitializer.SeedAsync(db, env, clock, hasher);
        await DbInitializer.SeedAsync(db, env, clock, hasher);

        var demoUsers = await db.Users
            .AsNoTracking()
            .Where(u => u.Email == "demo@example.com")
            .ToListAsync();
        demoUsers.Should().HaveCount(1);

        // MaintenanceDbContext has no global filter, so no IgnoreQueryFilters needed.
        var todos = await db.Todos
            .AsNoTracking()
            .Where(t => t.UserId == demoUsers[0].Id)
            .ToListAsync();
        todos.Should().HaveCount(500);
        var today = DateOnly.FromDateTime(fixedNow.Date);
        var dueDates = todos
            .Select(t => t.DueDate)
            .OfType<DateOnly>()
            .ToList();
        dueDates.Should().Contain(today);
        dueDates.Should().OnlyContain(d => d >= today.AddDays(-365) && d <= today.AddDays(365));
        dueDates.Min().Should().Be(today.AddDays(-365));
        dueDates.Max().Should().Be(today.AddDays(365));
        todos.Should().Contain(t => t.CompletedAt != null);
    }

    private static void ClearLogs(TestWebApplicationFactory factory)
    {
        lock (factory.StartupLogLines)
        {
            factory.StartupLogLines.Clear();
            factory.LogRecords.Clear();
        }
    }

    private static string AllLogBytes(TestWebApplicationFactory factory)
    {
        lock (factory.StartupLogLines)
        {
            var recordBytes = factory.LogRecords.Select(record =>
                record.Message + " " + string.Join(" ", record.Properties.Select(p => p.Key + "=" + p.Value)));
            return string.Join(Environment.NewLine, factory.StartupLogLines.Concat(recordBytes));
        }
    }

    private static bool PropertyEquals(CapturedLogRecord record, string key, string expected) =>
        record.Properties.TryGetValue(key, out var actual)
        && string.Equals(actual.Trim('"'), expected, StringComparison.OrdinalIgnoreCase);

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        response.Headers.GetValues(ContentTypeOptions).Should().Equal("nosniff");
        response.Headers.GetValues(FrameOptions).Should().Equal("DENY");
        response.Headers.GetValues(ReferrerPolicy).Should().Equal("no-referrer");
        response.Headers.GetValues(ContentSecurityPolicy).Should().Equal(
            "default-src 'none'; object-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'");
        response.Headers.GetValues(PermissionsPolicy).Should().Equal(
            "camera=(), microphone=(), geolocation=()");
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; } = now;
    }

    private sealed class FailingHealthCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            return Task.FromResult(HealthCheckResult.Unhealthy("forced"));
        }
    }
}
