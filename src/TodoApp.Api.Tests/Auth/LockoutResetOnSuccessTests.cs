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
/// Lockout reset semantics. A successful login zeroes <c>FailedLoginCount</c>,
/// clears <c>LockoutUntil</c>, and resets <c>LockoutLadderStep</c>. Otherwise a
/// user who got close to lockout once would carry that state forever, which
/// makes the ladder progressively hostile to honest users without adding any
/// security benefit.
/// </summary>
[Trait("Category", "Auth")]
public sealed class LockoutResetOnSuccessTests : IAsyncLifetime, IDisposable
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<TodoDbContext> _todoOptions = null!;
    private DbContextOptions<MaintenanceDbContext> _maintenanceOptions = null!;
    private const string Email = "reset@example.com";
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
    public async Task FourFails_ThenSuccess_ZeroesAllLockoutState()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero));
        var hasher = new PasswordHasher<User>();

        // Register.
        await using (var db = new TodoDbContext(_todoOptions, new AnonymousCurrentUser()))
        {
            var sut = new AuthService(db, hasher, clock);
            await sut.RegisterAsync(Email, Password);
        }

        // 4 wrong attempts — under the 5-fail threshold, no lockout armed yet.
        for (var i = 0; i < 4; i++)
        {
            await using var db = new TodoDbContext(_todoOptions, new AnonymousCurrentUser());
            var sut = new AuthService(db, hasher, clock);
            var act = async () => await sut.LoginAsync(Email, "wrong");
            await act.Should().ThrowAsync<InvalidOperationException>();
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        await using (var verify = new MaintenanceDbContext(_maintenanceOptions))
        {
            var user = await verify.Users.SingleAsync(u => u.Email == Email);
            user.FailedLoginCount.Should().Be(4, "four wrong attempts must accumulate to FailedLoginCount=4");
            user.LockoutUntil.Should().BeNull("under-threshold attempts must not arm lockout");
        }

        // Correct password — must zero everything.
        await using (var db = new TodoDbContext(_todoOptions, new AnonymousCurrentUser()))
        {
            var sut = new AuthService(db, hasher, clock);
            var user = await sut.LoginAsync(Email, Password);
            user.Email.Should().Be(Email);
        }

        await using (var verify = new MaintenanceDbContext(_maintenanceOptions))
        {
            var user = await verify.Users.SingleAsync(u => u.Email == Email);
            user.FailedLoginCount.Should().Be(0, "successful login must zero FailedLoginCount");
            user.LockoutUntil.Should().BeNull("successful login must clear LockoutUntil");
            user.LockoutLadderStep.Should().Be(0, "successful login must reset the ladder index — otherwise honest users carry strikes forever");
        }
    }

    private sealed class AnonymousCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => false;

        public Guid Id => throw new InvalidOperationException();

        public string Email => throw new InvalidOperationException();
    }
}
