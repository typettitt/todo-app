namespace TodoApp.Api.Data.Entities;

/// <summary>
/// Application user record. Email is stored lowercase + trimmed; <see cref="Role"/> is
/// server-set on register and never accepted from a request DTO.
/// </summary>
/// <remarks>
/// <see cref="FailedLoginCount"/>, <see cref="LockoutUntil"/>, and
/// <see cref="LockoutLadderStep"/> drive the per-account lockout ladder
/// (15m / 1h / 4h / 24h). Successful login zeroes all three; every 5
/// consecutive failures arms the next ladder step (clamped at 24h).
/// See <c>docs/decisions.md</c>.
/// </remarks>
public class User
{
    public Guid Id { get; set; }

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public Role Role { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Consecutive failed login attempts since the last success or lockout
    /// arming. Reset to 0 on successful login OR when a lockout fires (the
    /// ladder step advances; the failed-counter starts fresh for the next
    /// burst).
    /// </summary>
    public int FailedLoginCount { get; set; }

    /// <summary>
    /// Earliest UTC instant the account may attempt a login. Null when no
    /// lockout is active. While set and in the future, login returns the
    /// generic 401 — same body as wrong-password — so the lockout itself is
    /// not an oracle.
    /// </summary>
    public DateTimeOffset? LockoutUntil { get; set; }

    /// <summary>
    /// Index into the 15m / 1h / 4h / 24h ladder. Increments on each lockout
    /// arming; clamped at 3 (24h) at the top. Reset to 0 on successful login.
    /// </summary>
    public int LockoutLadderStep { get; set; }

    /// <summary>
    /// Application-managed concurrency token for login lockout state. Every
    /// failed-login or reset write increments it so parallel login attempts
    /// retry instead of overwriting one another's counters.
    /// </summary>
    public int LockoutVersion { get; set; }
}
