using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using TodoApp.Api.Data;
using TodoApp.Api.Data.Entities;
using TodoApp.Api.Features.Auth;

namespace TodoApp.Api.Tests.Auth;

/// <summary>
/// Unit tests for <see cref="AuthSessionService"/> covering the four state
/// transitions that drive the JwtBearer <c>OnTokenValidated</c> rejection logic:
/// create, validate, revoke, and the dual ExpiresAt / AbsoluteExpiresAt caps.
/// </summary>
/// <remarks>
/// Uses a persistent <c>:memory:</c> SQLite connection directly through
/// <see cref="MaintenanceDbContext"/>. Avoids <c>TestWebApplicationFactory</c>
/// because the validator/revoker logic is per-row and does not need the
/// HTTP pipeline.
/// </remarks>
[Trait("Category", "Auth")]
public sealed class AuthSessionTests : IAsyncLifetime, IDisposable
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<MaintenanceDbContext> _options = null!;
    private Guid _userId;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _options = new DbContextOptionsBuilder<MaintenanceDbContext>()
            .UseSqlite(_connection)
            .Options;

        // Seed a user so the AuthSession FK to Users can be satisfied.
        await using var seed = new MaintenanceDbContext(_options);
        await seed.Database.MigrateAsync();
        _userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        seed.Users.Add(new User
        {
            Id = _userId,
            Email = "session-tests@example.com",
            PasswordHash = "x",
            Role = Role.Basic,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await seed.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    public void Dispose() => _connection?.Dispose();

    [Fact]
    public async Task CreateAsync_PersistsRow_WithExpectedFields()
    {
        var jwtOptions = JwtOptionsFor(TimeSpan.FromMinutes(30));
        await using var db = new MaintenanceDbContext(_options);
        var sut = new AuthSessionService(db, jwtOptions);

        var before = DateTimeOffset.UtcNow;
        var session = await sut.CreateAsync(_userId);
        var after = DateTimeOffset.UtcNow;

        session.Sid.Should().NotBe(Guid.Empty);
        session.UserId.Should().Be(_userId);
        session.RevokedAt.Should().BeNull();
        session.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        session.ExpiresAt.Should().BeCloseTo(session.CreatedAt.AddMinutes(30), TimeSpan.FromSeconds(1));
        session.AbsoluteExpiresAt.Should().BeCloseTo(session.CreatedAt.AddDays(7), TimeSpan.FromSeconds(1));

        await using var verify = new MaintenanceDbContext(_options);
        var stored = await verify.AuthSessions.SingleAsync(s => s.Sid == session.Sid);
        stored.UserId.Should().Be(_userId);
        stored.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_ReturnsSession_ForFreshSid()
    {
        var jwtOptions = JwtOptionsFor(TimeSpan.FromMinutes(30));
        await using var db = new MaintenanceDbContext(_options);
        var sut = new AuthSessionService(db, jwtOptions);
        var created = await sut.CreateAsync(_userId);

        var validated = await sut.ValidateAsync(created.Sid);
        validated.Should().NotBeNull();
        validated!.Sid.Should().Be(created.Sid);
        validated.UserId.Should().Be(_userId);
    }

    [Fact]
    public async Task RevokeAsync_FlipsRevokedAt_AndSubsequentValidationFails()
    {
        var jwtOptions = JwtOptionsFor(TimeSpan.FromMinutes(30));
        await using var db = new MaintenanceDbContext(_options);
        var sut = new AuthSessionService(db, jwtOptions);
        var created = await sut.CreateAsync(_userId);

        var revoked = await sut.RevokeAsync(created.Sid);
        revoked.Should().BeTrue();

        await using var verify = new MaintenanceDbContext(_options);
        var stored = await verify.AuthSessions.AsNoTracking().SingleAsync(s => s.Sid == created.Sid);
        stored.RevokedAt.Should().NotBeNull();

        var validated = await sut.ValidateAsync(created.Sid);
        validated.Should().BeNull(
            "ValidateAsync must return null once the row is revoked — this is what powers logout");
    }

    [Fact]
    public async Task ValidateAsync_ReturnsNull_WhenExpiresAtIsInThePast()
    {
        var jwtOptions = JwtOptionsFor(TimeSpan.FromMinutes(30));
        await using var db = new MaintenanceDbContext(_options);
        var sut = new AuthSessionService(db, jwtOptions);
        var created = await sut.CreateAsync(_userId);

        // Force ExpiresAt into the past by writing directly through the maintenance
        // context (bypasses ExtendAsync which would clamp to AbsoluteExpiresAt).
        await using (var mutate = new MaintenanceDbContext(_options))
        {
            var row = await mutate.AuthSessions.SingleAsync(s => s.Sid == created.Sid);
            row.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-5);
            await mutate.SaveChangesAsync();
        }

        var validated = await sut.ValidateAsync(created.Sid);
        validated.Should().BeNull(
            "ExpiresAt < now must invalidate even though RevokedAt is null — JWT exp + session exp are independent invariants");
    }

    [Fact]
    public async Task ValidateAsync_ReturnsNull_WhenAbsoluteExpiresAtIsPast_EvenIfExpiresAtIsFuture()
    {
        var jwtOptions = JwtOptionsFor(TimeSpan.FromMinutes(30));
        await using var db = new MaintenanceDbContext(_options);
        var sut = new AuthSessionService(db, jwtOptions);
        var created = await sut.CreateAsync(_userId);

        // Push AbsoluteExpiresAt into the past while leaving ExpiresAt in the future.
        // This proves the absolute cap is checked independently of the rolling
        // ExpiresAt — a misbehaving renewal cannot extend past the absolute window.
        await using (var mutate = new MaintenanceDbContext(_options))
        {
            var row = await mutate.AuthSessions.SingleAsync(s => s.Sid == created.Sid);
            row.AbsoluteExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-5);
            row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
            await mutate.SaveChangesAsync();
        }

        var validated = await sut.ValidateAsync(created.Sid);
        validated.Should().BeNull(
            "AbsoluteExpiresAt is the absolute cap; ExpiresAt being in the future cannot rescue a session past Absolute");
    }

    [Fact]
    public async Task ExtendAsync_CapsExpiresAt_ToAbsoluteExpiresAt()
    {
        // Lifetime intentionally larger than the remaining absolute window so
        // the natural extension would overshoot — proves the min(...) clamp.
        var jwtOptions = JwtOptionsFor(TimeSpan.FromHours(2));

        Guid sid;
        await using (var createCtx = new MaintenanceDbContext(_options))
        {
            var creator = new AuthSessionService(createCtx, jwtOptions);
            var created = await creator.CreateAsync(_userId);
            sid = created.Sid;
        }

        // Reduce AbsoluteExpiresAt to ~30 seconds out so the 2-hour extension overshoots.
        var nearCap = DateTimeOffset.UtcNow.AddSeconds(30);
        await using (var mutate = new MaintenanceDbContext(_options))
        {
            var row = await mutate.AuthSessions.SingleAsync(s => s.Sid == sid);
            row.AbsoluteExpiresAt = nearCap;
            await mutate.SaveChangesAsync();
        }

        // Fresh context for the SUT so it sees the post-mutate AbsoluteExpiresAt
        // (EF Core's first-level cache would otherwise return the stale tracked
        // entity from the same DbContext that ran CreateAsync).
        await using var db = new MaintenanceDbContext(_options);
        var sut = new AuthSessionService(db, jwtOptions);
        var extended = await sut.ExtendAsync(sid);
        extended.Should().NotBeNull();
        extended!.ExpiresAt.Should().BeCloseTo(nearCap, TimeSpan.FromSeconds(1),
            "ExtendAsync must clamp ExpiresAt to AbsoluteExpiresAt rather than push past it");
    }

    private static IOptions<JwtOptions> JwtOptionsFor(TimeSpan lifetime)
        => Microsoft.Extensions.Options.Options.Create(new JwtOptions { Lifetime = lifetime });
}
