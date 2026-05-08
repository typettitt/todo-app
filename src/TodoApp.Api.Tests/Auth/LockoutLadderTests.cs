using FluentAssertions;

using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using TodoApp.Api.Data;
using TodoApp.Api.Data.Entities;
using TodoApp.Api.Features.Auth;
using TodoApp.Api.Features.Common;

namespace TodoApp.Api.Tests.Auth;

/// <summary>
/// Per-account lockout ladder. Five consecutive wrong-password attempts arms a
/// lockout; the lockout duration walks the <c>15m / 1h / 4h / 24h</c> ladder
/// and clamps at 24h. While locked, <see cref="AuthService.LoginAsync"/> always
/// throws <see cref="InvalidCredentialsException"/> — the locked-account path
/// is indistinguishable from wrong-password at the response-shape level.
/// </summary>
[Trait("Category", "Auth")]
public sealed class LockoutLadderTests : IAsyncLifetime, IDisposable
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<TodoDbContext> _todoOptions = null!;
    private DbContextOptions<MaintenanceDbContext> _maintenanceOptions = null!;
    private const string Email = "lockout@example.com";
    private const string Password = "Password1!";

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
    public async Task FiveFailsIn15min_LocksFor15min()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero));
        var hasher = new PasswordHasher<User>();
        await RegisterAsync(hasher, clock);

        // Five wrong-password attempts arm the first ladder step.
        for (var i = 0; i < 5; i++)
        {
            await ExpectInvalidCredentialsAsync(hasher, clock, Email, "wrong");
            clock.Advance(TimeSpan.FromSeconds(2));
        }

        await using (var verify = new MaintenanceDbContext(_maintenanceOptions))
        {
            var user = await verify.Users.SingleAsync(u => u.Email == Email);
            user.LockoutUntil.Should().NotBeNull();
            user.LockoutLadderStep.Should().Be(1, "first lockout arms ladder step 1 → next burst will use index 1 (1h)");
            user.LockoutUntil!.Value
                .Should().BeCloseTo(clock.Now.ToUniversalTime().AddMinutes(15).AddSeconds(-2), TimeSpan.FromSeconds(5),
                    "the 15-minute ladder rung is the first lockout duration");
        }

        // 6th attempt — locked. Even the CORRECT password returns the same
        // generic 401 (separate test verifies that explicitly).
        await ExpectInvalidCredentialsAsync(hasher, clock, Email, "wrong");
    }

    [Fact]
    public async Task LockoutLadder_15m_1h_4h_24h_Increments()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero));
        var hasher = new PasswordHasher<User>();
        await RegisterAsync(hasher, clock);

        var expectedRungs = new[]
        {
            TimeSpan.FromMinutes(15),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(24),
        };

        for (var rung = 0; rung < expectedRungs.Length; rung++)
        {
            // Five fails to arm the next rung.
            for (var i = 0; i < 5; i++)
            {
                await ExpectInvalidCredentialsAsync(hasher, clock, Email, "wrong");
                clock.Advance(TimeSpan.FromSeconds(1));
            }

            await using var verify = new MaintenanceDbContext(_maintenanceOptions);
            var user = await verify.Users.SingleAsync(u => u.Email == Email);
            user.LockoutUntil.Should().NotBeNull($"rung {rung} must arm lockout");
            var actualDuration = user.LockoutUntil!.Value - clock.Now.ToUniversalTime();
            actualDuration.Should().BeCloseTo(expectedRungs[rung], TimeSpan.FromSeconds(10),
                "rung {0} expected ~{1} but got {2}", rung, expectedRungs[rung], actualDuration);

            // Advance past the lockout window so the NEXT five fails arm the next rung.
            clock.Advance(expectedRungs[rung] + TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public async Task LockoutLadder_ClampsAt24Hours_OnFifthBurst()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero));
        var hasher = new PasswordHasher<User>();
        await RegisterAsync(hasher, clock);

        // Walk through 5 bursts; the 5th burst's lockout must still be 24h
        // (the clamp), not "longer than 24h."
        var expectedDurations = new[]
        {
            TimeSpan.FromMinutes(15),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(4),
            TimeSpan.FromHours(24),
            TimeSpan.FromHours(24),
        };

        foreach (var duration in expectedDurations)
        {
            for (var i = 0; i < 5; i++)
            {
                await ExpectInvalidCredentialsAsync(hasher, clock, Email, "wrong");
                clock.Advance(TimeSpan.FromSeconds(1));
            }

            await using (var verify = new MaintenanceDbContext(_maintenanceOptions))
            {
                var user = await verify.Users.SingleAsync(u => u.Email == Email);
                var actual = user.LockoutUntil!.Value - clock.Now.ToUniversalTime();
                actual.Should().BeCloseTo(duration, TimeSpan.FromSeconds(10),
                    "ladder must clamp at 24h on the 5th burst — running indefinitely up the ladder is what creates DoS");
            }

            clock.Advance(duration + TimeSpan.FromSeconds(1));
        }
    }

    private async Task RegisterAsync(IPasswordHasher<User> hasher, IClock clock)
    {
        await using var db = new TodoDbContext(_todoOptions, new AnonymousCurrentUser());
        var sut = new AuthService(db, hasher, clock);
        await sut.RegisterAsync(Email, Password);
    }

    private async Task ExpectInvalidCredentialsAsync(
        IPasswordHasher<User> hasher,
        IClock clock,
        string email,
        string password)
    {
        await using var db = new TodoDbContext(_todoOptions, new AnonymousCurrentUser());
        var sut = new AuthService(db, hasher, clock);
        var act = async () => await sut.LoginAsync(email, password);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class AnonymousCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => false;

        public Guid Id => throw new InvalidOperationException();

        public string Email => throw new InvalidOperationException();
    }
}

internal sealed class MutableClock : IClock
{
    private DateTimeOffset _now;

    public MutableClock(DateTimeOffset start) => _now = start;

    public DateTimeOffset Now => _now;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}
