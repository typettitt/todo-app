namespace TodoApp.Api.Data.Entities;

/// <summary>
/// Per-user todo record. <see cref="UserId"/> is server-set from
/// <c>ICurrentUser.Id</c> on create and never accepted from a request DTO. The
/// global query filter on <see cref="TodoApp.Api.Data.TodoDbContext"/> scopes every
/// query to the current user; cross-user access on <c>{id}</c> returns 404.
/// </summary>
public class Todo
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to <see cref="User"/>. Indexed; never client-supplied.</summary>
    public Guid UserId { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>
    /// Calendar date the todo is due. <see cref="DateOnly"/> deliberately — UTC
    /// instants for due dates are a known footgun across timezones.
    /// </summary>
    public DateOnly? DueDate { get; set; }

    public Priority Priority { get; set; } = Priority.Low;

    public bool IsCompleted { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string[] Tags { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optimistic-concurrency token bumped by <c>RowVersionInterceptor</c> on every
    /// tracked SaveChanges / SaveChangesAsync. <c>uint</c> is plenty for this workload
    /// and maps to SQLite INTEGER cleanly. The interceptor only fires for tracked
    /// saves — the CI grep gate keeps EF's bulk-update API away from this entity.
    /// </summary>
    public uint RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
