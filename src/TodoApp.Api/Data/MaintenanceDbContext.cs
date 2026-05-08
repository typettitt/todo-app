using Microsoft.EntityFrameworkCore;

using TodoApp.Api.Data.Entities;
using TodoApp.Api.Features.Common;

namespace TodoApp.Api.Data;

/// <summary>
/// Non-request database context. Mirrors the schema of <see cref="TodoDbContext"/>
/// (via <see cref="TodoModelConfiguration"/>) but applies NO row-scoping query
/// filter and has NO <see cref="ICurrentUser"/> dependency.
/// </summary>
/// <remarks>
/// Use this context only for paths that legitimately operate outside a user's
/// HTTP request:
/// <list type="bullet">
///   <item>EF Core migrations at startup (<c>DbInitializer.MigrateAsync</c>).</item>
///   <item>Development seeding (<c>DbInitializer.SeedAsync</c>).</item>
///   <item>The <c>/health/ready</c> database probe.</item>
/// </list>
/// Anything in an authenticated request path must use
/// <see cref="TodoDbContext"/>, whose fail-closed global filter guarantees row
/// isolation by user id. Splitting these two contexts makes the trust boundary
/// structural — there is no way to accidentally read another user's rows from a
/// request handler because the request handler's DI-resolved <c>DbContext</c>
/// always carries the filter.
/// </remarks>
public class MaintenanceDbContext : DbContext
{
    public MaintenanceDbContext(DbContextOptions<MaintenanceDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Todo> Todos => Set<Todo>();

    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        TodoModelConfiguration.Configure(modelBuilder, todoQueryFilter: null);
        base.OnModelCreating(modelBuilder);
    }
}
