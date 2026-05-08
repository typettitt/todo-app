using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TodoApp.Api.Data;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> tooling. Migrations are owned by
/// <see cref="MaintenanceDbContext"/> (no row-scoping filter, no
/// <c>ICurrentUser</c> dependency) so design-time scaffolding does not need to
/// thread a request principal through the ORM.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MaintenanceDbContext>
{
    public MaintenanceDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MaintenanceDbContext>()
            .UseSqlite("Data Source=todoapp.design.db")
            .Options;
        return new MaintenanceDbContext(options);
    }
}
