using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

using TodoApp.Api.Tests.Auth;

namespace TodoApp.Api.Tests.Infra;

/// <summary>
/// Verifies that <c>app.UseForwardedHeaders()</c> + <c>Trust:KnownProxies</c> partition the
/// rate limiter by the real client IP (the X-Forwarded-For value) when the request arrives
/// from a trusted upstream, and that the same header from an UNtrusted source is ignored
/// — i.e., a random client cannot escape its rate-limit bucket by forging the header.
/// </summary>
[Trait("Category", "Infra")]
public sealed class ForwardedHeadersTests
{
    /// <summary>
    /// Trust loopback as the upstream proxy. The TestServer connection IP is set to 127.0.0.1
    /// by a startup filter below, so X-Forwarded-For is honored. Two distinct forwarded IPs
    /// must land in two distinct rate-limit buckets — the second IP is NOT throttled by the
    /// first IP's exhausted budget.
    /// </summary>
    [Fact]
    public async Task TrustedProxy_RewritesRemoteIp_RateLimitPartitionsByForwardedFor()
    {
        await using var factory = new TestWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Trust:KnownProxies", "127.0.0.1");
                builder.UseSetting("RateLimits:Auth:EmailPermitLimit", "100");
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IStartupFilter, RemoteIpStartupFilter>();
                });
            });

        var clientA = factory.CreateClient();
        clientA.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.5");

        var statusesA = new List<HttpStatusCode>();
        for (var i = 0; i < 12; i++)
        {
            using var resp = await clientA.PostAsJsonAsync(
                "/api/auth/login",
                new { email = "nobody@example.com", password = "Password1!" });
            statusesA.Add(resp.StatusCode);
        }

        statusesA.Take(10).Should().OnlyContain(s => s == HttpStatusCode.Unauthorized,
            "first 10 attempts from 203.0.113.5 must consume that bucket without rate-limit");
        statusesA.Skip(10).Should().Contain(HttpStatusCode.TooManyRequests,
            "11th attempt from 203.0.113.5 must be rate-limited");

        // Different forwarded IP -> different bucket -> NOT rate-limited.
        var clientB = factory.CreateClient();
        clientB.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.6");
        using var firstFromB = await clientB.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "nobody@example.com", password = "Password1!" });
        firstFromB.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "203.0.113.6 has its own bucket and must not inherit 203.0.113.5's exhausted budget");
    }

    /// <summary>
    /// Trust an IP that does NOT match the connection IP. UseForwardedHeaders sees the request
    /// arrives from loopback (untrusted, since defaults are cleared once `Trust:KnownProxies`
    /// is set), ignores X-Forwarded-For, and the limiter partitions by the connection IP only.
    /// All 12 requests share one bucket — i.e., spoofing X-Forwarded-For does not let you
    /// escape rate-limiting.
    /// </summary>
    [Fact]
    public async Task UntrustedSource_DoesNotHonorForwardedFor_FallsBackToConnectionIp()
    {
        await using var factory = new TestWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                // 10.99.99.99 is the only trusted proxy; loopback is NOT in the list and the
                // defaults are cleared because the config key is set. Loopback-originated
                // requests are therefore untrusted, X-Forwarded-For is ignored.
                builder.UseSetting("Trust:KnownProxies", "10.99.99.99");
                builder.UseSetting("RateLimits:Auth:EmailPermitLimit", "100");
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IStartupFilter, RemoteIpStartupFilter>();
                });
            });

        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 12; i++)
        {
            // Each request varies the spoofed forwarded IP — if X-Forwarded-For were honored,
            // these would land in 12 distinct buckets and none would be rate-limited.
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new { email = "nobody@example.com", password = "Password1!" }),
            };
            req.Headers.Add("X-Forwarded-For", $"198.51.100.{i + 1}");
            using var resp = await client.SendAsync(req);
            statuses.Add(resp.StatusCode);
        }

        // Spoofing fails: all 12 share the loopback bucket -> last few must be 429.
        statuses.Skip(10).Should().Contain(HttpStatusCode.TooManyRequests,
            "X-Forwarded-For from an untrusted source must be ignored; the connection IP is the rate-limit key");
    }

    /// <summary>
    /// TestServer leaves <c>HttpContext.Connection.RemoteIpAddress</c> null by default. The
    /// rate limiter and ForwardedHeaders middleware both depend on a non-null connection IP,
    /// so this filter sets it to loopback at the very front of the pipeline — emulating the
    /// "request arrived from nginx on the same docker network" case.
    /// </summary>
    private sealed class RemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            => app =>
            {
                app.Use(async (ctx, nxt) =>
                {
                    ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
                    await nxt();
                });
                next(app);
            };
    }
}
