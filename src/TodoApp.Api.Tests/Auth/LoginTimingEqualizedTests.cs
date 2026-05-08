using System.Diagnostics;

using FluentAssertions;

using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using TodoApp.Api.Data;
using TodoApp.Api.Data.Entities;
using TodoApp.Api.Features.Auth;
using TodoApp.Api.Features.Common;

namespace TodoApp.Api.Tests.Auth;

/// <summary>
/// Timing-oracle elimination. Login miss path (user not found) and login
/// wrong-password path must run the SAME PBKDF2 verify so a network observer
/// cannot enumerate accounts via response time.
/// </summary>
/// <remarks>
/// This test calls <see cref="AuthService.LoginAsync"/> directly (not via the
/// HTTP pipeline) so the auth-rate-limit policy at <c>10/min/IP</c> doesn't
/// interfere with the 30-sample baseline. Calling the service in-process also
/// strips out HTTP plumbing variance, leaving PBKDF2 cost as the dominant
/// signal.
/// <para>
/// CI noise can still make this flaky. The threshold is intentionally
/// generous (60ms) — PBKDF2 costs ~30ms per call on commodity hardware, so
/// the typical delta is well under that. If this regresses, the right fix is
/// to investigate the regression, NOT to widen the threshold further.
/// </para>
/// </remarks>
[Trait("Category", "Auth")]
public sealed class LoginTimingEqualizedTests : IAsyncLifetime, IDisposable
{
    private const int SampleCount = 30;
    private const int WarmupCount = 5;
    private static readonly TimeSpan MaxMeanDelta = TimeSpan.FromMilliseconds(60);

    private SqliteConnection _connection = null!;
    private DbContextOptions<TodoDbContext> _todoOptions = null!;
    private DbContextOptions<MaintenanceDbContext> _maintenanceOptions = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _todoOptions = new DbContextOptionsBuilder<TodoDbContext>()
            .UseSqlite(_connection)
            .Options;
        _maintenanceOptions = new DbContextOptionsBuilder<MaintenanceDbContext>()
            .UseSqlite(_connection)
            .Options;

        await using var seed = new MaintenanceDbContext(_maintenanceOptions);
        await seed.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    public void Dispose() => _connection?.Dispose();

    [Fact]
    public async Task Login_MissAndWrongPassword_TimingDelta_IsSmall()
    {
        var hasher = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions
        {
            IterationCount = 600_000,
        }));
        var clock = new SystemClock();

        // Seed a real user with a real PBKDF2 hash so the wrong-password path
        // runs the actual verify cost, not a no-op.
        const string realEmail = "real@example.com";
        const string realPassword = "Password1!";
        await using (var seed = new MaintenanceDbContext(_maintenanceOptions))
        {
            var seedUser = new User
            {
                Id = Guid.NewGuid(),
                Email = realEmail,
                Role = Role.Basic,
                CreatedAt = clock.Now,
                UpdatedAt = clock.Now,
            };
            seedUser.PasswordHash = hasher.HashPassword(seedUser, realPassword);
            seed.Users.Add(seedUser);
            await seed.SaveChangesAsync();
        }

        var missDurations = new List<long>(SampleCount);
        var wrongDurations = new List<long>(SampleCount);

        // Warm up — first PBKDF2 call after process start is dramatically slower
        // (JIT, lazy dummy-hash construction). Discarding the warmup samples is
        // the classic micro-bench discipline; we want steady-state.
        for (var i = 0; i < WarmupCount; i++)
        {
            await TimedLoginAttemptAsync(hasher, clock, "no-such@example.com", "any-password");
            await TimedLoginAttemptAsync(hasher, clock, realEmail, "wrong-password");
            await ResetLoginFailureStateAsync(realEmail);
        }

        for (var i = 0; i < SampleCount; i++)
        {
            // Interleave to soak up any per-iteration drift evenly.
            missDurations.Add(await TimedLoginAttemptAsync(hasher, clock, $"no-such-{i}@example.com", "any-password"));
            wrongDurations.Add(await TimedLoginAttemptAsync(hasher, clock, realEmail, "wrong-password"));
            await ResetLoginFailureStateAsync(realEmail);
        }

        // Stopwatch.ElapsedTicks runs on Stopwatch's frequency, NOT TimeSpan
        // ticks (100ns). Convert through Stopwatch.Frequency to get a real
        // duration; otherwise we'd be off by orders of magnitude on most
        // platforms.
        var missMean = TicksToTimeSpan(missDurations.Average());
        var wrongMean = TicksToTimeSpan(wrongDurations.Average());
        var delta = (missMean - wrongMean).Duration();

        delta.Should().BeLessThan(MaxMeanDelta,
            "miss-mean = {0} ms, wrong-pw mean = {1} ms; if PBKDF2 dominates both branches, the delta should be well under {2} ms",
            missMean.TotalMilliseconds,
            wrongMean.TotalMilliseconds,
            MaxMeanDelta.TotalMilliseconds);
    }

    private static TimeSpan TicksToTimeSpan(double stopwatchTicks)
        => TimeSpan.FromSeconds(stopwatchTicks / Stopwatch.Frequency);

    private async Task<long> TimedLoginAttemptAsync(
        IPasswordHasher<User> hasher,
        IClock clock,
        string email,
        string password)
    {
        // Fresh DbContext per call — mirrors per-request scope and stops EF's
        // first-level cache from making the wrong-password path artificially
        // faster on the second call.
        await using var db = new TodoDbContext(_todoOptions, new TestNullCurrentUser());
        var sut = new AuthService(db, hasher, clock);

        var sw = Stopwatch.StartNew();
        try
        {
            await sut.LoginAsync(email, password);
        }
        catch (InvalidOperationException)
        {
            // EmailAlreadyExistsException / InvalidCredentialsException both
            // derive from InvalidOperationException; we expect the latter.
        }

        sw.Stop();
        return sw.ElapsedTicks;
    }

    private async Task ResetLoginFailureStateAsync(string email)
    {
        await using var db = new MaintenanceDbContext(_maintenanceOptions);
        var user = await db.Users.SingleAsync(u => u.Email == email);
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        user.LockoutLadderStep = 0;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Anonymous principal stand-in for the request-scoped TodoDbContext. The
    /// fail-closed query filter routes through <see cref="ICurrentUser.IsAuthenticated"/>;
    /// returning false makes the filter resolve to <c>Guid.Empty</c> and shows
    /// no rows, which is fine because <see cref="AuthService"/> reads
    /// <c>Users</c>, which has no global filter.
    /// </summary>
    private sealed class TestNullCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => false;

        public Guid Id => throw new InvalidOperationException("anonymous");

        public string Email => throw new InvalidOperationException("anonymous");
    }
}
