using Microsoft.Extensions.Options;

namespace TodoApp.Api.Features.Auth;

/// <summary>
/// Centralizes cookie issuance + clearing for the auth cookie. Login, sliding renewal,
/// and logout MUST go through these helpers — when delete attributes diverge from set
/// attributes by even one byte the browser keeps the original cookie.
/// </summary>
public sealed class AuthCookies(IOptions<JwtOptions> options, IHostEnvironment env)
{
    private readonly JwtOptions _options = options.Value;
    private readonly IHostEnvironment _env = env;

    public string CookieName => _options.CookieName;

    public void Set(HttpContext httpContext, string token, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.Cookies.Append(_options.CookieName, token, BuildOptions(expiresAt));
    }

    public void Clear(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // Delete with byte-identical attributes; an Expires-in-the-past Append matches what
        // browsers actually require — Cookies.Delete() omits Expires which can leave ghosts.
        var opts = BuildOptions(DateTimeOffset.UnixEpoch);
        httpContext.Response.Cookies.Append(_options.CookieName, string.Empty, opts);
    }

    public CookieOptions BuildOptions(DateTimeOffset expiresAt)
    {
        var isDev = _env.IsDevelopment();
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = !isDev,
            SameSite = isDev ? SameSiteMode.Lax : _options.CookieSameSite,
            Path = "/",
            Expires = expiresAt,
            IsEssential = true,
        };
    }
}
