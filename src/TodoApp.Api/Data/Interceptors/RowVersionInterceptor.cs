using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using TodoApp.Api.Data.Entities;
using TodoApp.Api.Features.Common;

namespace TodoApp.Api.Data.Interceptors;

/// <summary>
/// Bumps <see cref="Todo.RowVersion"/> and stamps <see cref="Todo.CreatedAt"/> /
/// <see cref="Todo.UpdatedAt"/> on every tracked save.
/// </summary>
/// <remarks>
/// NOTE: This interceptor only fires for SaveChanges / SaveChangesAsync. EF Core's
/// bulk-update API (ExecuteUpdate / ExecuteUpdateAsync) bypasses the change tracker
/// entirely. The CI grep gate keeps that API away from <c>Todo</c> writes.
/// </remarks>
public sealed class RowVersionInterceptor(IClock clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Stamp(eventData.Context, clock.Now);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Stamp(eventData.Context, clock.Now);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Stamp(DbContext? context, DateTimeOffset now)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<Todo>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.CreatedAt == default)
                    {
                        entry.Entity.CreatedAt = now;
                    }

                    if (entry.Entity.UpdatedAt == default)
                    {
                        entry.Entity.UpdatedAt = now;
                    }

                    if (entry.Entity.RowVersion == 0)
                    {
                        entry.Entity.RowVersion = 1;
                    }

                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.RowVersion = checked(entry.Entity.RowVersion + 1);
                    break;

                case EntityState.Detached:
                case EntityState.Unchanged:
                case EntityState.Deleted:
                default:
                    break;
            }
        }
    }
}
