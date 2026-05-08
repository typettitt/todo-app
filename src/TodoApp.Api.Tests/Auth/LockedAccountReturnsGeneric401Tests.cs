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
/// Locked-account-no-oracle behavior. While locked, login returns the SAME
/// <see cref="InvalidCredentialsException"/> as wrong-password: no "account
/// locked" string in the response, and even the CORRECT password returns the
/// same generic 401. The lockout state is invisible to the caller.
/// </summary>
[Trait("Category", "Auth")]
public sealed class LockedAccountReturnsGeneric401Tests : IAsyncLifetime, IDisposable
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<TodoDbContext> _todoOptions = null!;
    private DbContextOptions<MaintenanceDbContext> _maintenanceOptions = null!;
    private const string Email = "lockedonly@example.com";
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
    public async Task LockedAccount_WrongPassword_ThrowsGenericInvalidCredentials()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero));
        var hasher = new PasswordHasher<User>();
        await ArrangeLockoutAsync(hasher, clock);

        await using var db = new TodoDbContext(_todoOptions, new AnonymousCurrentUser());
        var sut = new AuthService(db, hasher, clock);
        var act = async () => await sut.LoginAsync(Email, "still-wrong");

        var thrown = await act.Should().ThrowAsync<InvalidCredentialsException>();
        thrown.And.Message.Should().NotContainAny("lock", "Lock", "LOCK", "lockout", "locked");
    }

    [Fact]
    public async Task LockedAccount_CorrectPassword_StillReturnsGeneric401()
    {
        // Deliberately the most counter-intuitive part of the lockout design:
        // the CORRECT password during lockout STILL returns 401. Otherwise the
        // attacker has a "you got it right but the account is locked"
        // signal, which is just account enumeration with extra steps.
        var clock = new MutableClock(new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero));
        var hasher = new PasswordHasher<User>();
        await ArrangeLockoutAsync(hasher, clock);

        await using var db = new TodoDbContext(_todoOptions, new AnonymousCurrentUser());
        var sut = new AuthService(db, hasher, clock);
        var act = async () => await sut.LoginAsync(Email, Password);

        await act.Should().ThrowAsync<InvalidCredentialsException>(
            "while locked, even the correct password must produce the SAME generic 401 — the lockout supersedes correctness");
    }

    [Fact]
    public async Task LockedAccount_AfterLockoutWindow_CorrectPasswordSucceeds()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero));
        var hasher = new PasswordHasher<User>();
        await ArrangeLockoutAsync(hasher, clock);

        // Roll past the 15-minute lockout window.
        clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));

        await using (var db = new TodoDbContext(_todoOptions, new AnonymousCurrentUser()))
        {
            var sut = new AuthService(db, hasher, clock);
            var user = await sut.LoginAsync(Email, Password);
            user.Email.Should().Be(Email);
        }

        // Successful login resets the ladder back to zero.
        await using var verify = new MaintenanceDbContext(_maintenanceOptions);
        var stored = await verify.Users.SingleAsync(u => u.Email == Email);
        stored.LockoutUntil.Should().BeNull();
        stored.FailedLoginCount.Should().Be(0);
        stored.LockoutLadderStep.Should().Be(0);
    }

    private async Task ArrangeLockoutAsync(IPasswordHasher<User> hasher, MutableClock clock)
    {
        await using (var db = new TodoDbContext(_todoOptions, new AnonymousCurrentUser()))
        {
            var sut = new AuthService(db, hasher, clock);
            await sut.RegisterAsync(Email, Password);
        }

        // Five fails arms the first ladder step (15 min).
        for (var i = 0; i < 5; i++)
        {
            await using var db = new TodoDbContext(_todoOptions, new AnonymousCurrentUser());
            var sut = new AuthService(db, hasher, clock);
            var act = async () => await sut.LoginAsync(Email, "wrong");
            await act.Should().ThrowAsync<InvalidOperationException>();
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        await using var verify = new MaintenanceDbContext(_maintenanceOptions);
        var user = await verify.Users.SingleAsync(u => u.Email == Email);
        user.LockoutUntil.Should().NotBeNull("arrange step must arm the lockout before the assertion phase");
    }

    private sealed class AnonymousCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => false;

        public Guid Id => throw new InvalidOperationException();

        public string Email => throw new InvalidOperationException();
    }
}
