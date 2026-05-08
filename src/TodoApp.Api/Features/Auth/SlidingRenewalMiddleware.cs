using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

using TodoApp.Api.Data;
using TodoApp.Api.Features.Common;

namespace TodoApp.Api.Features.Auth;

/// <summary>
/// Re-issues the auth cookie when the current token has crossed its renewal threshold,
/// throttled per <c>sub</c> via <see cref="IMemoryCache"/> so a burst of parallel
/// authenticated requests cannot produce competing <c>Set-Cookie</c> responses (the
/// classic flicker-logout race).
/// </summary>
public sealed class SlidingRenewalMiddleware(
    RequestDelegate next,
    IMemoryCache cache,
    IOptions<JwtOptions> options,
    JwtTokenService tokens,
    AuthCookies cookies,
    ILogger<SlidingRenewalMiddleware> logger)
{
    private const string CacheKeyPrefix = "auth:renewed:";

    private readonly RequestDelegate _next = next;
    private readonly IMemoryCache _cache = cache;
    private readonly JwtOptions _options = options.Value;
    private readonly JwtTokenService _tokens = tokens;
    private readonly AuthCookies _cookies = cookies;
    private readonly ILogger<SlidingRenewalMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        await TryRenewAsync(context).ConfigureAwait(false);
        await _next(context).ConfigureAwait(false);
    }

    private async Task TryRenewAsync(HttpContext context)
    {
        try
        {
            var user = context.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                return;
            }

            var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (sub is null || !Guid.TryParse(sub, out var userId))
            {
                return;
            }

            // Every authenticated cookie carries a `sid` claim. If it is
            // missing or malformed, do not issue a new cookie. The token
            // validator should already have failed the request; this branch is
            // defense-in-depth.
            var sidClaim = user.FindFirstValue("sid");
            if (sidClaim is null || !Guid.TryParse(sidClaim, out var sid))
            {
                return;
            }

            var expClaim = user.FindFirstValue(JwtRegisteredClaimNames.Exp);
            var iatClaim = user.FindFirstValue(JwtRegisteredClaimNames.Iat);
            if (expClaim is null
                || iatClaim is null
                || !long.TryParse(expClaim, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var exp)
                || !long.TryParse(iatClaim, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var iat))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(exp);
            var issuedAt = DateTimeOffset.FromUnixTimeSeconds(iat);
            if (now >= expiresAt)
            {
                return;
            }

            var fullLifetime = expiresAt - issuedAt;
            var remaining = expiresAt - now;
            if (remaining > fullLifetime * _options.RenewalThreshold)
            {
                return;
            }

            var key = CacheKeyPrefix + userId;
            if (!TryReserveRenewal(key))
            {
                return;
            }

            // Reload the user so the renewed token reflects current Email/Role.
            var dbContext = context.RequestServices.GetRequiredService<TodoDbContext>();
            var account = await dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId, context.RequestAborted)
                .ConfigureAwait(false);
            if (account is null)
            {
                return;
            }

            // Extend the server-side session row's ExpiresAt up to the absolute
            // cap. If ExtendAsync returns null the session was just revoked or
            // crossed AbsoluteExpiresAt — do NOT issue a renewed cookie; let the
            // next request 401 through OnTokenValidated.
            var sessions = context.RequestServices.GetRequiredService<AuthSessionService>();
            var extended = await sessions.ExtendAsync(sid, context.RequestAborted).ConfigureAwait(false);
            if (extended is null)
            {
                return;
            }

            var token = _tokens.IssueToken(account, sid, now);
            _cookies.Set(context, token, now.Add(_options.Lifetime));
        }
#pragma warning disable CA1031 // Renewal is best-effort — never fail the real request.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.SlidingRenewalFailed(ex);
        }
    }

    private bool TryReserveRenewal(string key)
    {
        if (_cache.TryGetValue(key, out _))
        {
            return false;
        }

        lock (_cache)
        {
            if (_cache.TryGetValue(key, out _))
            {
                return false;
            }

            using var entry = _cache.CreateEntry(key);
            entry.AbsoluteExpirationRelativeToNow = _options.RenewalThrottle;
            entry.Value = true;
            return true;
        }
    }
}
