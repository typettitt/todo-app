using System.Runtime.CompilerServices;
using System.Text;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using TodoApp.Api.Data;
using TodoApp.Api.Data.Entities;
using TodoApp.Api.Features.Common;

namespace TodoApp.Api.Features.Auth;

/// <summary>
/// Pure account-management logic — register and login — sitting between the auth
/// endpoints and the data layer. Email is normalized (lowercase + trim) before any
/// lookup or persist; new users are always <see cref="Role.Basic"/>.
/// </summary>
/// <remarks>
/// Hardening lives here:
/// <list type="bullet">
///   <item>The <see cref="LoginAsync"/> miss path runs the SAME PBKDF2 verify
///   that the wrong-password path runs (against a dummy fixed hash) so the
///   network-observable response time of "no such email" is statistically
///   indistinguishable from "wrong password." DO NOT REMOVE that branch — it
///   is not dead code.</item>
///   <item>Per-account lockout ladder (15m / 1h / 4h / 24h). On 5 consecutive
///   failures the account is locked for the current ladder step. While locked,
///   <see cref="LoginAsync"/> ALWAYS throws <see cref="InvalidCredentialsException"/>
///   even when the supplied password is correct — this prevents a "you got
///   the password right but the account is locked" oracle. Successful login
///   AFTER lockout expires resets the ladder.</item>
/// </list>
/// See <c>docs/decisions.md</c> "Auth response equalization + per-account lockout"
/// for the full rationale.
/// </remarks>
public sealed class AuthService
{
    private const string DummyPassword = "dummy-password-equalize-pbkdf2-cost";

    private static readonly User DummyUser = new()
    {
        Id = Guid.Empty,
        Email = "dummy@local",
        Role = Role.Basic,
        CreatedAt = DateTimeOffset.MinValue,
        UpdatedAt = DateTimeOffset.MinValue,
        PasswordHash = string.Empty,
    };

    private static readonly ConditionalWeakTable<IPasswordHasher<User>, Lazy<string>> DummyHashes = new();

    /// <summary>
    /// Lockout ladder. Index 0 → first lockout (15m); index 3 and beyond → 24h
    /// (clamped). The clamp guarantees the ladder cannot decay into perpetual
    /// lockout while still slowing a sustained credential-stuffing campaign to
    /// roughly four guesses per day.
    /// </summary>
    internal static readonly TimeSpan[] LockoutLadder =
    {
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(4),
        TimeSpan.FromHours(24),
    };

    /// <summary>
    /// Threshold of consecutive failed login attempts that arms the next
    /// ladder step.
    /// </summary>
    internal const int FailedLoginThreshold = 5;

    private const int MaxLoginStateRetries = 32;

    private readonly TodoDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly IClock _clock;
    private readonly string _dummyHash;

    /// <summary>
    /// Process-wide dummy hash used by the <see cref="LoginAsync"/> miss path
    /// to equalize PBKDF2 cost. Must be a real hash from the active hasher —
    /// hardcoded bytes would skip the same iteration count the real path runs
    /// and therefore would not equalize.
    /// </summary>
    /// <remarks>
    /// DO NOT REMOVE. The dummy hash equalizes PBKDF2 cost between the
    /// "user not found" and "user found, wrong password" branches of
    /// <see cref="LoginAsync"/>. Without it, a network-position attacker could
    /// distinguish "no account with this email" from "account exists but
    /// password is wrong" by measuring response time alone, regardless of
    /// what the response body says.
    /// </remarks>

    public AuthService(TodoDbContext db, IPasswordHasher<User> hasher, IClock clock)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
        _dummyHash = GetOrCreateDummyHash(hasher);
    }

    public async Task<User> RegisterAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeEmail(email);
        var existing = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            throw new EmailAlreadyExistsException(normalized);
        }

        var now = _clock.Now.ToUniversalTime();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalized,
            Role = Role.Basic, // server-set; never trust DTO
            CreatedAt = now,
            UpdatedAt = now,
        };
        user.PasswordHash = _hasher.HashPassword(user, password);

        _db.Users.Add(user);
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Race: another request inserted the same email between the lookup and SaveChanges.
            if (await EmailExistsAsync(normalized, cancellationToken).ConfigureAwait(false))
            {
                throw new EmailAlreadyExistsException(normalized);
            }

            throw;
        }

        return user;
    }

    public async Task<User> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeEmail(email);
        for (var attempt = 0; attempt <= MaxLoginStateRetries; attempt++)
        {
            var user = await _db.Users
                .FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken)
                .ConfigureAwait(false);

            if (user is null)
            {
                // Equalize PBKDF2 cost. DO NOT REMOVE — without this branch the
                // miss path is observably faster than the wrong-password path and
                // attackers can enumerate accounts via response time. Discard the
                // result; the outcome is always "credentials invalid."
                _ = _hasher.VerifyHashedPassword(DummyUser, _dummyHash, password);
                throw new InvalidCredentialsException();
            }

            var now = _clock.Now.ToUniversalTime();

            // Lockout check runs before password verification. A locked account
            // returns the same generic 401 as wrong-password. We deliberately
            // do not increment FailedLoginCount while locked: the lockout is
            // already armed, and counting more during the lockout window would
            // let an attacker walk the account up the ladder for free.
            if (user.LockoutUntil is { } until && now < until)
            {
                throw new InvalidCredentialsException();
            }

            var verification = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
            if (verification == PasswordVerificationResult.Failed)
            {
                ApplyFailedLogin(user, now);

                try
                {
                    await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    throw new InvalidCredentialsException();
                }
                catch (DbUpdateConcurrencyException) when (attempt < MaxLoginStateRetries)
                {
                    _db.ChangeTracker.Clear();
                    continue;
                }
            }

            if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = _hasher.HashPassword(user, password);
            }

            // Successful login resets the entire ladder. Whether the account
            // had 4/5 strikes or had cleared a previous lockout, the user is
            // back to a fresh slate.
            if (user.FailedLoginCount != 0
                || user.LockoutUntil is not null
                || user.LockoutLadderStep != 0
                || verification == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.FailedLoginCount = 0;
                user.LockoutUntil = null;
                user.LockoutLadderStep = 0;
                user.LockoutVersion = checked(user.LockoutVersion + 1);
                user.UpdatedAt = now;

                try
                {
                    await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (DbUpdateConcurrencyException) when (attempt < MaxLoginStateRetries)
                {
                    _db.ChangeTracker.Clear();
                    continue;
                }
            }

            return user;
        }

        throw new InvalidOperationException("Could not update login state after concurrent retries.");
    }

    private static void ApplyFailedLogin(User user, DateTimeOffset now)
    {
        user.FailedLoginCount += 1;
        if (user.FailedLoginCount >= FailedLoginThreshold)
        {
            var stepIndex = Math.Min(user.LockoutLadderStep, LockoutLadder.Length - 1);
            user.LockoutUntil = now.Add(LockoutLadder[stepIndex]);
            user.LockoutLadderStep = Math.Min(user.LockoutLadderStep + 1, LockoutLadder.Length);
            user.FailedLoginCount = 0; // ladder is armed; restart counting for the next burst
        }

        user.LockoutVersion = checked(user.LockoutVersion + 1);
        user.UpdatedAt = now;
    }

    private async Task<bool> EmailExistsAsync(string normalized, CancellationToken cancellationToken)
        => await _db.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == normalized, cancellationToken)
            .ConfigureAwait(false);

    private static string GetOrCreateDummyHash(IPasswordHasher<User> hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);

        return DummyHashes.GetValue(
            hasher,
            static h => new Lazy<string>(
                () => h.HashPassword(DummyUser, DummyPassword),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    // Apply Unicode NFC BEFORE casing so combining-vs-precomposed accent forms
    // (e.g. "café" written with U+0065 U+0301 vs U+00E9) collapse to the same
    // record key. Without NFC the same human-visible address can register two
    // separate accounts. See docs/decisions.md.
    internal static string NormalizeEmail(string? email) =>
        (email ?? string.Empty).Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant();
}
