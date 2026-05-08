using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

namespace TodoApp.Api.Tests.Auth;

/// <summary>
/// Register-enumeration-oracle elimination. The register endpoint returns the
/// SAME 200 body for both branches:
/// <list type="bullet">
///   <item>New email → cookie issued + body <c>{ "status": "received" }</c>.</item>
///   <item>Duplicate email → no cookie + body <c>{ "status": "received" }</c>.</item>
/// </list>
/// The cookie-presence asymmetry is the FE's signal (it triggers a /me probe);
/// it is invisible to a network-position attacker behind TLS, which is what
/// closes the body-shape oracle.
/// </summary>
[Trait("Category", "Auth")]
public sealed class RegisterReturnsUniformBodyTests : IAsyncLifetime, IDisposable
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
    public async Task Register_NewEmail_Returns200_WithUniformBody()
    {
        var client = _factory.CreateClient();

        var response = await client.RegisterAsync("fresh@example.com", "Password1!");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("received");

        // No leaking of email/role/id — those fields are the enumeration oracle.
        body.TryGetProperty("email", out _).Should().BeFalse();
        body.TryGetProperty("role", out _).Should().BeFalse();
        body.TryGetProperty("id", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns200_WithIdenticalBody()
    {
        var client = _factory.CreateClient();
        var first = await client.RegisterAsync("dup@example.com", "Password1!");
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.RegisterAsync("dup@example.com", "DifferentPassword2@");

        second.StatusCode.Should().Be(HttpStatusCode.OK,
            "the duplicate-email branch must mirror the new-email branch's status code exactly");
        second.Content.Headers.ContentType?.MediaType.Should().Be("application/json",
            "the duplicate branch must NOT leak application/problem+json — that itself would be an oracle");

        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        secondBody.GetProperty("status").GetString().Should().Be(
            firstBody.GetProperty("status").GetString(),
            "byte-shape parity between the two branches is the whole point");
        secondBody.GetRawText().Should().Be(firstBody.GetRawText());
    }

    [Fact]
    public async Task Register_NewEmail_AlsoSetsCookie()
    {
        var client = _factory.CreateClient();

        var response = await client.RegisterAsync("cookie-on@example.com", "Password1!");

        response.GetAuthCookieValue().Should().NotBeNullOrWhiteSpace(
            "the new-email branch must issue an auth cookie so the FE /me probe lights up immediately");
    }

    [Fact]
    public async Task Register_DuplicateEmail_DoesNotSetCookie()
    {
        var client = _factory.CreateClient();
        await client.RegisterAsync("cookie-off@example.com", "Password1!");

        var second = await client.RegisterAsync("cookie-off@example.com", "DifferentPassword2@");

        second.GetAuthCookieValue().Should().BeNull(
            "duplicate-email branch must NOT issue a session cookie — that's the FE-only signal that registration was a no-op");
    }
}
