using System.Threading.RateLimiting;

namespace TodoApp.Api.Features.Auth;

/// <summary>
/// In-memory per-account limiter for login attempts. The IP limiter remains in
/// middleware; this limiter caps distributed attempts against one normalized
/// email address after the request body has been bound and validated.
/// </summary>
public sealed class LoginEmailRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public LoginEmailRateLimiter(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var permitLimit = Math.Max(
            1,
            configuration.GetValue<int>("RateLimits:Auth:EmailPermitLimit", 10));
        var windowSeconds = Math.Max(
            1,
            configuration.GetValue<int>("RateLimits:Auth:EmailWindowSeconds", 60));

        _limiter = PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromSeconds(windowSeconds),
            }));
    }

    public bool TryAcquire(string normalizedEmail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);

        using var lease = _limiter.AttemptAcquire(normalizedEmail, permitCount: 1);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
