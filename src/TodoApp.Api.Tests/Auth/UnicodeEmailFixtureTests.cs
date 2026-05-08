using System.Net;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using TodoApp.Api.Data;
using TodoApp.Api.Features.Auth;

namespace TodoApp.Api.Tests.Auth;

/// <summary>
/// <see cref="AuthService.NormalizeEmail"/> applies NFC before lowercasing, so
/// two byte-distinct but visually-identical
/// renderings of the same accented address collapse to a single record key.
/// Without NFC, the combining form (e + U+0301) registers a SECOND user that
/// the precomposed form (U+00E9) cannot reach via login — silent account
/// separation.
/// </summary>
[Trait("Category", "Auth")]
public sealed class UnicodeEmailFixtureTests : IAsyncLifetime, IDisposable
{
    // Built via explicit escapes so the source-file encoding cannot silently
    // normalize either literal away from the form we want to assert on.
    // CombiningForm: "caf" + "e" + U+0301 (NFD). PrecomposedForm: "caf" + U+00E9 (NFC).
    private const string CombiningForm = "cafe\u0301@example.com";
    private const string PrecomposedForm = "caf\u00E9@example.com";

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
    public void NormalizeEmail_CombiningAndPrecomposed_NormalizeToSameString()
    {
        // Sanity check — without NFC, these two strings are NOT equal even
        // though they render identically. NFC collapses them before casing.
        CombiningForm.Should().NotBe(PrecomposedForm);

        var combiningNormalized = AuthService.NormalizeEmail(CombiningForm);
        var precomposedNormalized = AuthService.NormalizeEmail(PrecomposedForm);

        combiningNormalized.Should().Be(precomposedNormalized);
    }

    [Fact]
    public async Task Register_CombiningAccent_ThenRegister_PrecomposedAccent_HitsDuplicateBranch()
    {
        var client = _factory.CreateClient();

        var first = await client.RegisterAsync(CombiningForm, "Password1!");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.GetAuthCookieValue().Should().NotBeNullOrWhiteSpace();

        // The precomposed form must collapse to the SAME record key — duplicate
        // branch, no cookie issued.
        var second = await client.RegisterAsync(PrecomposedForm, "DifferentPassword2@");
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        second.GetAuthCookieValue().Should().BeNull(
            "the precomposed form must hit the duplicate-email branch and skip cookie issuance");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
        (await db.Users.CountAsync()).Should().Be(1, "homograph forms must collapse to a single user row");
    }

    [Fact]
    public async Task Register_PrecomposedAccent_ThenLogin_CombiningAccent_Succeeds()
    {
        var registerClient = _factory.CreateClient();
        var register = await registerClient.RegisterAsync(PrecomposedForm, "Password1!");
        register.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginClient = _factory.CreateClient();
        var login = await loginClient.LoginAsync(CombiningForm, "Password1!");

        login.StatusCode.Should().Be(HttpStatusCode.OK,
            "combining-form login must reach the same record the precomposed registration created");
        login.GetAuthCookieValue().Should().NotBeNullOrWhiteSpace();
    }
}
