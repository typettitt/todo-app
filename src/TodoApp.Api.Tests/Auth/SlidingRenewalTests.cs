using System.Net;

using FluentAssertions;

namespace TodoApp.Api.Tests.Auth;

[Trait("Category", "Auth")]
public sealed class SlidingRenewalTests
{
    [Fact]
    public async Task SlidingRenewal_DoesNotRenewAnonymousRequest()
    {
        await using var factory = new TestWebApplicationFactory(
            jwt =>
            {
                jwt.Lifetime = TimeSpan.FromSeconds(2);
                jwt.RenewalThreshold = 0.9; // would always renew if user were authenticated
                jwt.RenewalThrottle = TimeSpan.FromMilliseconds(50);
            });
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.GetSetCookieHeaders().Should().BeEmpty(
            "no auth cookie should be issued for an unauthenticated request");
    }

    [Fact]
    public async Task SlidingRenewal_DoesNotRenewWhenWellAboveThreshold()
    {
        await using var factory = new TestWebApplicationFactory(
            jwt =>
            {
                jwt.Lifetime = TimeSpan.FromMinutes(30);
                jwt.RenewalThreshold = 0.5;
                jwt.RenewalThrottle = TimeSpan.FromMilliseconds(50);
            });
        var client = factory.CreateClient();
        var register = await client.RegisterAsync("alice@example.com", "Password1!");
        var initialCookie = register.GetAuthCookieValue();

        var meReq = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/auth/me", UriKind.Relative));
        meReq.Headers.Add("Cookie", $"auth={initialCookie}");
        var meResp = await client.SendAsync(meReq);
        meResp.StatusCode.Should().Be(HttpStatusCode.OK);

        meResp.GetSetCookieHeaders()
            .Where(h => h.StartsWith("auth=", StringComparison.Ordinal))
            .Should()
            .BeEmpty("token still has the bulk of its lifetime");
    }

    [Fact]
    public async Task AuthedRequestBurst_TriggersExactlyOneRenewal_PerWindow()
    {
        await using var factory = new TestWebApplicationFactory(
            jwt =>
            {
                jwt.Lifetime = TimeSpan.FromSeconds(2);
                jwt.RenewalThreshold = 1.0;
                jwt.RenewalThrottle = TimeSpan.FromMinutes(1);
            });
        var bootstrap = factory.CreateClient();
        var register = await bootstrap.RegisterAsync("alice@example.com", "Password1!");
        var cookie = register.GetAuthCookieValue();

        var first = await SendMeWithCookieAsync(factory, cookie);
        var followUps = Enumerable.Range(0, 4)
            .Select(_ => SendMeWithCookieAsync(factory, cookie))
            .ToArray();

        var responses = new[] { first }.Concat(await Task.WhenAll(followUps)).ToArray();
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);

        var renewedHeaders = responses
            .SelectMany(r => r.GetSetCookieHeaders())
            .Where(h => h.StartsWith("auth=", StringComparison.Ordinal) && !h.StartsWith($"auth={cookie};", StringComparison.Ordinal))
            .ToArray();

        renewedHeaders.Length.Should().Be(1,
            "the per-sub renewal throttle must collapse parallel renewals into exactly one");
    }

    private static Task<HttpResponseMessage> SendMeWithCookieAsync(
        TestWebApplicationFactory factory,
        string? cookie)
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/auth/me", UriKind.Relative));
        request.Headers.Add("Cookie", $"auth={cookie}");
        return client.SendAsync(request);
    }
}
