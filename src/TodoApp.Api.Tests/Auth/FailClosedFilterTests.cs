using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using TodoApp.Api.Data;
using TodoApp.Api.Data.Entities;
using TodoApp.Api.Features.Common;

namespace TodoApp.Api.Tests.Auth;

/// <summary>
/// Structural tests for the trust boundary between request-scoped
/// <see cref="TodoDbContext"/> (fail-CLOSED row filter) and
/// <see cref="MaintenanceDbContext"/> (no filter, no <see cref="ICurrentUser"/>).
/// </summary>
/// <remarks>
/// Builds a single SQLite-in-memory connection, seeds two users + two todos via
/// <see cref="MaintenanceDbContext"/>, then queries the same database through
/// each context to assert the boundary in both directions.
/// </remarks>
[Trait("Category", "Ownership")]
public sealed class FailClosedFilterTests
{
    [Fact]
    public async Task Anonymous_TodoDbContext_Read_ReturnsEmpty()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await SeedTwoUsersAsync(connection);

        var requestOptions = new DbContextOptionsBuilder<TodoDbContext>()
            .UseSqlite(connection)
            .Options;

        // Unauthenticated: ICurrentUser.IsAuthenticated == false → filter resolves
        // to t.UserId == Guid.Empty, which matches no row. Fail-CLOSED.
        await using var anonymous = new TodoDbContext(requestOptions, new StubCurrentUser());

        var rows = await anonymous.Todos.AsNoTracking().ToListAsync();
        rows.Should().BeEmpty(
            "request-scoped context with no authenticated user must return zero rows " +
            "(the previous fail-OPEN disjunction returned every user's rows)");
    }

    [Fact]
    public async Task Authenticated_TodoDbContext_Read_ReturnsOnlyOwnedRows()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var (aliceId, _) = await SeedTwoUsersAsync(connection);

        var requestOptions = new DbContextOptionsBuilder<TodoDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var aliceCtx = new TodoDbContext(requestOptions, new StubCurrentUser(aliceId));

        var rows = await aliceCtx.Todos.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(1);
        rows.Single().UserId.Should().Be(aliceId);
    }

    [Fact]
    public async Task Maintenance_TodoDbContext_Read_ReturnsAll()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await SeedTwoUsersAsync(connection);

        var maintenanceOptions = new DbContextOptionsBuilder<MaintenanceDbContext>()
            .UseSqlite(connection)
            .Options;

        // Maintenance context has no filter → migrations and seeds see every row.
        await using var maintenance = new MaintenanceDbContext(maintenanceOptions);

        var rows = await maintenance.Todos.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(2);
        rows.Select(t => t.Title).Should().BeEquivalentTo(new[] { "alice-todo", "bob-todo" });
    }

    private static async Task<(Guid AliceId, Guid BobId)> SeedTwoUsersAsync(SqliteConnection connection)
    {
        var maintenanceOptions = new DbContextOptionsBuilder<MaintenanceDbContext>()
            .UseSqlite(connection)
            .Options;

        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var seed = new MaintenanceDbContext(maintenanceOptions);
        await seed.Database.MigrateAsync();

        seed.Users.AddRange(
            new User
            {
                Id = aliceId,
                Email = "alice@example.com",
                PasswordHash = "x",
                Role = Role.Basic,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new User
            {
                Id = bobId,
                Email = "bob@example.com",
                PasswordHash = "x",
                Role = Role.Basic,
                CreatedAt = now,
                UpdatedAt = now,
            });

        seed.Todos.AddRange(
            new Todo
            {
                Id = Guid.NewGuid(),
                UserId = aliceId,
                Title = "alice-todo",
                Tags = Array.Empty<string>(),
                RowVersion = 1,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new Todo
            {
                Id = Guid.NewGuid(),
                UserId = bobId,
                Title = "bob-todo",
                Tags = Array.Empty<string>(),
                RowVersion = 1,
                CreatedAt = now,
                UpdatedAt = now,
            });

        await seed.SaveChangesAsync();
        return (aliceId, bobId);
    }

    private sealed class StubCurrentUser : ICurrentUser
    {
        private readonly Guid? _id;

        public StubCurrentUser()
        {
            _id = null;
        }

        public StubCurrentUser(Guid id)
        {
            _id = id;
        }

        public bool IsAuthenticated => _id.HasValue;

        public Guid Id => _id ?? throw new InvalidOperationException("Not authenticated.");

        public string Email => _id.HasValue
            ? "stub@example.com"
            : throw new InvalidOperationException("Not authenticated.");
    }
}
