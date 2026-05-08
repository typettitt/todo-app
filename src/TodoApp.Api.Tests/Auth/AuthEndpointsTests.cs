using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using TodoApp.Api.Data;
using TodoApp.Api.Data.Entities;
using TodoApp.Api.Features.Auth;

namespace TodoApp.Api.Tests.Auth;

[Trait("Category", "Auth")]
public sealed class AuthEndpointsTests : IAsyncLifetime, IDisposable
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
    public async Task Register_Succeeds_AndIssuesCookie()
    {
        var client = _factory.CreateClient();

        var response = await client.RegisterAsync("alice@example.com", "Password1!");

        // Register returns 200 + uniform { status: "received" } for BOTH
        // success and duplicate. Cookie presence is the only
        // network-observable distinguisher.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.GetAuthCookieValue().Should().NotBeNullOrWhiteSpace();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("received");
    }

    [Fact]
    public async Task Register_LowercasesEmail_AndCaseInsensitiveLoginWorks()
    {
        var client = _factory.CreateClient();
        var register = await client.RegisterAsync("Alice@Example.COM", "Password1!");
        register.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        (await db.Users.SingleAsync()).Email.Should().Be("alice@example.com");

        var loginClient = _factory.CreateClient();
        var login = await loginClient.LoginAsync("ALICE@example.com", "Password1!");
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        login.GetAuthCookieValue().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Register_RoleField_InRequest_IsIgnored_AlwaysBasic()
    {
        var client = _factory.CreateClient();

        var response = await client.RegisterAsync(
            "newuser@example.com",
            "Password1!",
            extraFields: new { role = "Admin" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        var user = await db.Users.SingleAsync();
        user.Role.Should().Be(Role.Basic);
    }

    [Fact]
    public async Task Register_WhitespacePassword_Returns400()
    {
        var client = _factory.CreateClient();

        var response = await client.RegisterAsync("alice@example.com", "        ");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var pd = await response.Content.ReadFromJsonAsync<JsonElement>();
        pd.GetProperty("errors").GetProperty("password").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Register_PasswordTooShort_Returns400()
    {
        var client = _factory.CreateClient();

        var response = await client.RegisterAsync("alice@example.com", "short");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var pd = await response.Content.ReadFromJsonAsync<JsonElement>();
        pd.GetProperty("errors").GetProperty("password").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401_WithCanonicalProblemDetails()
    {
        var client = _factory.CreateClient();
        await client.RegisterAsync("alice@example.com", "Password1!");

        var login = await client.LoginAsync("alice@example.com", "WrongPassword!");

        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        login.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var pd = await login.Content.ReadFromJsonAsync<JsonElement>();
        pd.GetProperty("status").GetInt32().Should().Be(401);
        pd.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Me_WithoutCookie_Returns401_NoWWWAuthenticateHeader()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Should().BeEmpty(
            "JwtBearer's default WWW-Authenticate: Bearer header must be suppressed for cookie auth");
    }

    [Fact]
    public async Task Me_WithCookie_ReturnsClaims()
    {
        var client = _factory.CreateClient();
        var register = await client.RegisterAsync("alice@example.com", "Password1!");
        var cookie = register.GetAuthCookieValue();

        var meRequest = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/auth/me", UriKind.Relative));
        meRequest.Headers.Add("Cookie", $"auth={cookie}");

        var response = await client.SendAsync(meRequest);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("email").GetString().Should().Be("alice@example.com");
        body.GetProperty("role").GetString().Should().Be("Basic");
    }

    [Fact]
    public async Task Me_WithSignedTokenWhoseSubAndSidBelongToDifferentUsers_Returns401()
    {
        var client = _factory.CreateClient();
        (await client.RegisterAsync("alice@example.com", "Password1!")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.RegisterAsync("bob@example.com", "Password1!")).StatusCode.Should().Be(HttpStatusCode.OK);

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
            var tokens = scope.ServiceProvider.GetRequiredService<JwtTokenService>();
            var alice = await db.Users.AsNoTracking().SingleAsync(u => u.Email == "alice@example.com");
            var bob = await db.Users.AsNoTracking().SingleAsync(u => u.Email == "bob@example.com");
            var bobSession = await db.AuthSessions
                .AsNoTracking()
                .SingleAsync(s => s.UserId == bob.Id);

            token = tokens.IssueToken(alice, bobSession.Sid, DateTimeOffset.UtcNow);
        }

        var authClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/auth/me", UriKind.Relative));
        request.Headers.Add("Cookie", $"auth={token}");
        using var response = await authClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_DeletesCookie_WithIdenticalAttributes()
    {
        var client = _factory.CreateClient();
        var register = await client.RegisterAsync("alice@example.com", "Password1!");
        var loginSetCookie = register.GetSetCookieHeaders()
            .Single(h => h.StartsWith("auth=", StringComparison.Ordinal));

        var logoutReq = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/auth/logout", UriKind.Relative));
        logoutReq.Headers.Add("Cookie", $"auth={register.GetAuthCookieValue()}");
        var logout = await client.SendAsync(logoutReq);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var logoutSetCookie = logout.GetSetCookieHeaders()
            .Single(h => h.StartsWith("auth=", StringComparison.Ordinal));

        var loginAttrs = TestHelpers.ParseCookieAttributes(loginSetCookie);
        var logoutAttrs = TestHelpers.ParseCookieAttributes(logoutSetCookie);

        // Path / SameSite / Secure / HttpOnly must match byte-for-byte.
        loginAttrs.GetValueOrDefault("path").Should().Be(logoutAttrs.GetValueOrDefault("path"));
        loginAttrs.GetValueOrDefault("samesite").Should().Be(logoutAttrs.GetValueOrDefault("samesite"));
        loginAttrs.ContainsKey("httponly").Should().Be(logoutAttrs.ContainsKey("httponly"));
        loginAttrs.ContainsKey("secure").Should().Be(logoutAttrs.ContainsKey("secure"));
    }

    [Fact]
    public async Task Login_ProductionCookie_UsesSecureHttpOnlyStrictFlags()
    {
        var signingKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
        await using var factory = new TestWebApplicationFactory(
            environmentName: "Production",
            signingKey: signingKey,
            configuration: new Dictionary<string, string?>
            {
                ["RateLimits:Auth:PermitLimit"] = "100",
                ["RateLimits:Auth:EmailPermitLimit"] = "100",
                ["Internal:HealthHeader"] = "test-health-secret",
            });
        var client = factory.CreateClient();

        var response = await client.RegisterAsync("prod-cookie@example.com", "Password1!");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var setCookie = response.GetSetCookieHeaders().Single(h => h.StartsWith("auth=", StringComparison.Ordinal));
        var attrs = TestHelpers.ParseCookieAttributes(setCookie);
        attrs.ContainsKey("httponly").Should().BeTrue();
        attrs.ContainsKey("secure").Should().BeTrue();
        string.Equals(attrs.GetValueOrDefault("samesite"), "Strict", StringComparison.OrdinalIgnoreCase)
            .Should()
            .BeTrue();
        attrs.GetValueOrDefault("path").Should().Be("/");
    }

    [Fact]
    public async Task RateLimit_Login_Blocks_After_10_PerMinute_PerIp()
    {
        var client = _factory.CreateClient();

        // Hit /login 12 times with no register first, so the entire 10/min budget is
        // available for these requests. Failures return 401; once the budget is gone
        // the limiter takes over with 429.
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 12; i++)
        {
            var resp = await client.LoginAsync("nobody@example.com", "Password1!");
            statuses.Add(resp.StatusCode);
        }

        statuses.Take(10).Should().OnlyContain(s => s == HttpStatusCode.Unauthorized);
        statuses.Skip(10).Should().Contain(HttpStatusCode.TooManyRequests);

        // 429 responses use the canonical ProblemDetails envelope.
        var rateLimited = statuses.Skip(10).First();
        rateLimited.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task RateLimit_Login_Blocks_ByNormalizedEmail_EvenWhenIpBudgetRemains()
    {
        await using var factory = new TestWebApplicationFactory(configuration: new Dictionary<string, string?>
        {
            ["RateLimits:Auth:PermitLimit"] = "100",
            ["RateLimits:Auth:EmailPermitLimit"] = "2",
            ["RateLimits:Auth:EmailWindowSeconds"] = "60",
        });
        var client = factory.CreateClient();

        var first = await client.LoginAsync("TARGET@example.com", "wrong");
        var second = await client.LoginAsync(" target@example.com ", "wrong");
        var third = await client.LoginAsync("target@example.com", "wrong");

        first.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Login_ConcurrentWrongPasswords_PreservesEveryFailedAttempt()
    {
        await using var factory = new TestWebApplicationFactory(configuration: new Dictionary<string, string?>
        {
            ["RateLimits:Auth:PermitLimit"] = "100",
            ["RateLimits:Auth:EmailPermitLimit"] = "100",
        });
        var setup = factory.CreateClient();
        (await setup.RegisterAsync("parallel@example.com", "Password1!")).StatusCode.Should().Be(HttpStatusCode.OK);

        var tasks = Enumerable.Range(0, 4)
            .Select(_ => factory.CreateClient().LoginAsync("parallel@example.com", "WrongPassword1!"))
            .ToArray();

        var responses = await Task.WhenAll(tasks);
        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.Unauthorized);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == "parallel@example.com");
        user.FailedLoginCount.Should().Be(4);
        user.LockoutUntil.Should().BeNull();
        user.LockoutVersion.Should().Be(4);
    }
}
