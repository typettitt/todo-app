using System.Net;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using TodoApp.Api.Data;

namespace TodoApp.Api.Tests.Auth;

/// <summary>
/// End-to-end tests for logout-actually-invalidates-the-session behavior.
/// Verifies that a captured cookie pre-logout returns 401 after
/// <c>POST /api/auth/logout</c>, that anonymous logout still 204s, and that
/// the sliding-renewal extension never pushes ExpiresAt past AbsoluteExpiresAt.
/// </summary>
[Trait("Category", "Auth")]
public sealed class LogoutRevokesSessionTests
{
    [Fact]
    public async Task Login_Then_Logout_Then_ReplayCookie_Returns401()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();
        var register = await client.RegisterAsync("alice@example.com", "Password1!");
        register.StatusCode.Should().Be(HttpStatusCode.OK);
        var capturedCookie = register.GetAuthCookieValue();
        capturedCookie.Should().NotBeNullOrEmpty();

        // Sanity: the cookie works before logout.
        var meBefore = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/auth/me", UriKind.Relative));
        meBefore.Headers.Add("Cookie", $"auth={capturedCookie}");
        var meBeforeResp = await client.SendAsync(meBefore);
        meBeforeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var logoutReq = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/auth/logout", UriKind.Relative));
        logoutReq.Headers.Add("Cookie", $"auth={capturedCookie}");
        var logout = await client.SendAsync(logoutReq);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Replay the original cookie. The JWT signature is still valid; the
        // server-side session row was revoked, so OnTokenValidated fails closed.
        var meAfter = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/auth/me", UriKind.Relative));
        meAfter.Headers.Add("Cookie", $"auth={capturedCookie}");
        var meAfterResp = await client.SendAsync(meAfter);
        meAfterResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the captured cookie must stop working after logout — JWT validity is necessary but not sufficient");

        meAfterResp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Logout_Anonymous_Returns204_NoSessionRevocation()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        var logoutReq = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/auth/logout", UriKind.Relative));
        var logout = await client.SendAsync(logoutReq);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "logout is a fire-and-forget UX and must 204 even with no cookie attached");

        // Confirm no session row was revoked (none existed) — the anonymous path
        // must not write to the AuthSessions table.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
        (await db.AuthSessions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SlidingRenewal_DoesNotExceedAbsoluteLifetime()
    {
        // Tight JWT lifetime + low threshold + short throttle so renewal triggers
        // immediately on the next /me request.
        await using var factory = new TestWebApplicationFactory(
            jwt =>
            {
                jwt.Lifetime = TimeSpan.FromSeconds(2);
                jwt.RenewalThreshold = 0.99;
                jwt.RenewalThrottle = TimeSpan.FromMilliseconds(50);
            });

        var client = factory.CreateClient();
        var register = await client.RegisterAsync("alice@example.com", "Password1!");
        register.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = register.GetAuthCookieValue();
        cookie.Should().NotBeNullOrEmpty();

        // Locate the freshly created session and force AbsoluteExpiresAt very
        // close to "now" so the next renewal MUST clamp ExpiresAt rather than
        // push it 2 seconds out.
        Guid sid;
        DateTimeOffset cap;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
            var session = await db.AuthSessions.SingleAsync();
            sid = session.Sid;
            cap = DateTimeOffset.UtcNow.AddSeconds(1);
            session.AbsoluteExpiresAt = cap;
            await db.SaveChangesAsync();
        }

        // Trigger sliding renewal by calling /me with the captured cookie.
        var meReq = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/auth/me", UriKind.Relative));
        meReq.Headers.Add("Cookie", $"auth={cookie}");
        var me = await client.SendAsync(meReq);
        me.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
            var refreshed = await db.AuthSessions.AsNoTracking().SingleAsync(s => s.Sid == sid);
            refreshed.ExpiresAt.Should().BeOnOrBefore(cap.AddMilliseconds(50),
                "sliding renewal must clamp ExpiresAt to AbsoluteExpiresAt — the absolute cap is the upper bound on session lifetime");
        }
    }
}
