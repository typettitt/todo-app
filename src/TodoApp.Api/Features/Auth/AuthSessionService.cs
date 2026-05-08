using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using TodoApp.Api.Data;
using TodoApp.Api.Data.Entities;

namespace TodoApp.Api.Features.Auth;

/// <summary>
/// Owns the <see cref="AuthSession"/> lifecycle: create on register/login,
/// validate on every authenticated request (via the JwtBearer
/// <c>OnTokenValidated</c> event), extend on sliding renewal, and revoke on
/// logout. The JWT alone has no invalidation channel; this service is what
/// turns "JWT in cookie" into a revocable session.
/// </summary>
/// <remarks>
/// Constructor takes <see cref="MaintenanceDbContext"/>, NOT
/// <see cref="TodoDbContext"/>. Sessions cross users — the JwtBearer validator
/// needs to read whatever <c>sid</c> the inbound cookie carries, regardless of
/// which user owns it. Routing through the request-scoped context with its
/// row-scoping query filter would hide other users' sessions during validation
/// and make <see cref="RevokeAsync"/> unable to flip rows it cannot see. Row
/// scoping is enforced on <see cref="Todo"/> only.
/// </remarks>
public sealed class AuthSessionService(
    MaintenanceDbContext db,
    IOptions<JwtOptions> jwtOptions)
{
    private readonly MaintenanceDbContext _db = db;
    private readonly JwtOptions _jwt = jwtOptions.Value;

    /// <summary>
    /// Absolute cap from session creation. Every renewal is bounded by this
    /// upper limit so an actively-used session cannot live indefinitely.
    /// </summary>
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(7);

    public async Task<AuthSession> CreateAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new AuthSession
        {
            Sid = Guid.NewGuid(),
            UserId = userId,
            CreatedAt = now,
            ExpiresAt = now.Add(_jwt.Lifetime),
            AbsoluteExpiresAt = now.Add(AbsoluteLifetime),
            RevokedAt = null,
        };

        _db.AuthSessions.Add(session);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return session;
    }

    public async Task<AuthSession?> ValidateAsync(Guid sid, CancellationToken ct = default)
    {
        var session = await _db.AuthSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Sid == sid, ct)
            .ConfigureAwait(false);
        if (session is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (session.RevokedAt is not null
            || now > session.ExpiresAt
            || now > session.AbsoluteExpiresAt)
        {
            return null;
        }

        return session;
    }

    public async Task<bool> RevokeAsync(Guid sid, CancellationToken ct = default)
    {
        var session = await _db.AuthSessions
            .FirstOrDefaultAsync(s => s.Sid == sid, ct)
            .ConfigureAwait(false);
        if (session is null)
        {
            return false;
        }

        if (session.RevokedAt is not null)
        {
            // Already revoked; treat as idempotent success.
            return true;
        }

        session.RevokedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<int> RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var sessions = await _db.AuthSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (sessions.Count == 0)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var session in sessions)
        {
            session.RevokedAt = now;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return sessions.Count;
    }

    /// <summary>
    /// Used by sliding renewal. Sets <c>ExpiresAt = min(now + JwtOptions.Lifetime,
    /// AbsoluteExpiresAt)</c> so renewals can keep a session alive within the
    /// absolute cap, never past it. Returns null if the session is missing or
    /// already invalid (revoked / past absolute cap) — the caller must NOT
    /// re-issue a cookie in that case.
    /// </summary>
    public async Task<AuthSession?> ExtendAsync(Guid sid, CancellationToken ct = default)
    {
        var session = await _db.AuthSessions
            .FirstOrDefaultAsync(s => s.Sid == sid, ct)
            .ConfigureAwait(false);
        if (session is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (session.RevokedAt is not null || now > session.AbsoluteExpiresAt)
        {
            return null;
        }

        var proposed = now.Add(_jwt.Lifetime);
        session.ExpiresAt = proposed > session.AbsoluteExpiresAt
            ? session.AbsoluteExpiresAt
            : proposed;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return session;
    }
}
