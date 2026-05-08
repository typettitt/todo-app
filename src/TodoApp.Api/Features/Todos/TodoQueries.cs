using Microsoft.EntityFrameworkCore;

using TodoApp.Api.Data.Entities;

namespace TodoApp.Api.Features.Todos;

/// <summary>
/// IQueryable extensions for the todo list endpoint. Composable so the endpoint
/// reads top-down: filter → search → sort → paging.
/// </summary>
public static class TodoQueries
{
    public static IQueryable<Todo> WithStatus(
        this IQueryable<Todo> source,
        TodoStatusFilter status,
        DateOnly? today)
    {
        ArgumentNullException.ThrowIfNull(source);

        return status switch
        {
            TodoStatusFilter.All => source,
            TodoStatusFilter.Active => source.Where(t => !t.IsCompleted),
            TodoStatusFilter.Completed => source.Where(t => t.IsCompleted),
            TodoStatusFilter.DueToday =>
                today.HasValue
                    ? source.Where(t => t.DueDate == today.Value)
                    : throw new ArgumentException(
                        "DueToday filter requires a non-null 'today'. Endpoint must validate before calling.",
                        nameof(today)),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown status filter."),
        };
    }

    public static IQueryable<Todo> WithSearch(this IQueryable<Todo> source, string? q)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(q))
        {
            return source;
        }

        // Escape SQL LIKE meta-characters so a search containing `%` or `_`
        // matches literally instead of wildcard-scanning every row.
        // SQLite only treats `\` as the escape character when ESCAPE is
        // declared explicitly via the third arg to EF.Functions.Like.
        var escaped = q
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        var pattern = $"%{escaped}%";
        return source.Where(t =>
            EF.Functions.Like(t.Title, pattern, "\\")
            || (t.Description != null && EF.Functions.Like(t.Description, pattern, "\\")));
    }

    public static IQueryable<Todo> WithDueWindow(
        this IQueryable<Todo> source,
        DateOnly? dueFrom,
        DateOnly? dueTo)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (dueFrom.HasValue)
        {
            source = source.Where(t => t.DueDate >= dueFrom.Value);
        }

        if (dueTo.HasValue)
        {
            source = source.Where(t => t.DueDate <= dueTo.Value);
        }

        return source;
    }

    public static IQueryable<Todo> WithSort(
        this IQueryable<Todo> source,
        SortBy by,
        SortDir dir)
    {
        ArgumentNullException.ThrowIfNull(source);

        IOrderedQueryable<Todo> ordered = (by, dir) switch
        {
            (SortBy.CreatedAt, SortDir.Asc) => source.OrderBy(t => t.CreatedAt),
            (SortBy.CreatedAt, SortDir.Desc) => source.OrderByDescending(t => t.CreatedAt),
            (SortBy.DueDate, SortDir.Asc) => source.OrderBy(t => t.DueDate),
            (SortBy.DueDate, SortDir.Desc) => source.OrderByDescending(t => t.DueDate),
            (SortBy.Priority, SortDir.Asc) => source.OrderBy(t =>
                t.Priority == Priority.Low ? 0 : t.Priority == Priority.Medium ? 1 : 2),
            (SortBy.Priority, SortDir.Desc) => source.OrderByDescending(t =>
                t.Priority == Priority.Low ? 0 : t.Priority == Priority.Medium ? 1 : 2),
            (SortBy.Title, SortDir.Asc) => source.OrderBy(t => t.Title),
            (SortBy.Title, SortDir.Desc) => source.OrderByDescending(t => t.Title),
            _ => source.OrderByDescending(t => t.CreatedAt),
        };

        // Always tiebreak on Id so paging is deterministic when the primary sort key
        // collides (same CreatedAt, same DueDate, etc.).
        return ordered.ThenBy(t => t.Id);
    }

    public static IQueryable<Todo> WithPaging(this IQueryable<Todo> source, int page, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Skip((page - 1) * pageSize).Take(pageSize);
    }
}
