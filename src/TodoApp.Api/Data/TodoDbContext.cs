using Microsoft.EntityFrameworkCore;

using TodoApp.Api.Data.Entities;
using TodoApp.Api.Features.Common;

namespace TodoApp.Api.Data;

/// <summary>
/// Request-scoped database context. Owns <see cref="User"/> and <see cref="Todo"/>.
/// The <see cref="Todo"/> set is row-scoped to the current user via a global
/// query filter anchored on <see cref="ICurrentUser"/>.
/// </summary>
/// <remarks>
/// Anything that needs to operate OUTSIDE an HTTP request — EF migrations,
/// dev seeding, the <c>/health/ready</c> probe, design-time tooling — must use
/// <see cref="MaintenanceDbContext"/> instead. That context applies the same
/// schema with no row filter and no <see cref="ICurrentUser"/> dependency.
/// </remarks>
public class TodoDbContext : DbContext
{
    private readonly ICurrentUser _currentUser;

    public TodoDbContext(DbContextOptions<TodoDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Todo> Todos => Set<Todo>();

    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    /// <summary>
    /// Backstop for the global query filter when the captured expression is
    /// reified by EF Core's parameter extraction. Returns <see cref="Guid.Empty"/>
    /// when no user is authenticated. Combined with the fail-closed filter
    /// <c>t.UserId == CurrentUserIdOrEmpty()</c>, an unauthenticated read sees
    /// zero rows because <see cref="Guid.Empty"/> is never a real
    /// <see cref="Todo.UserId"/>. This replaces the previous
    /// <c>!IsAuthenticated || …</c> short-circuit which fell OPEN and exposed
    /// every user's rows on anonymous reads.
    /// </summary>
    private Guid CurrentUserIdOrEmpty() => _currentUser.IsAuthenticated ? _currentUser.Id : Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Row-scoping global filter — fail-CLOSED. When unauthenticated,
        // CurrentUserIdOrEmpty() is Guid.Empty which matches no row, so anonymous
        // reads return empty rather than every user's rows. EF eagerly evaluates
        // closure members during parameter extraction (it does NOT honor C#
        // short-circuit semantics on the captured expression), so we route through
        // a single helper instead of guarding with a disjunction. Non-request paths
        // that legitimately need the unfiltered view (migrations, seed,
        // /health/ready) use MaintenanceDbContext, not this context.
        TodoModelConfiguration.Configure(
            modelBuilder,
            todoQueryFilter: t => t.UserId == CurrentUserIdOrEmpty());

        base.OnModelCreating(modelBuilder);
    }
}
