using TodoApp.Api.Data.Entities;

namespace TodoApp.Api.Features.Todos;

/// <summary>
/// Create payload. Server-set fields (Id, UserId, CreatedAt, UpdatedAt, RowVersion,
/// IsCompleted) are intentionally absent — the server populates them. <c>Priority</c>
/// and <c>Tags</c> default to <see cref="Priority.Low"/> and <c>[]</c> when null.
/// </summary>
public sealed record CreateTodoRequest(
    string Title,
    string? Description,
    DateOnly? DueDate,
    Priority? Priority,
    string[]? Tags);

/// <summary>
/// Full-replacement update payload. PUT semantics — every field must round-trip.
/// </summary>
/// <remarks>
/// CRITICAL behavior: PUT is full replace, not patch. Omitted JSON properties are
/// bound as default values: <c>description</c> absent → null (clears existing),
/// <c>dueDate</c> absent → null (clears existing), <c>tags</c> absent → empty array
/// (clears existing). The validator messages document this so clients see the
/// intent on validation failures. <c>RowVersion</c> is required for optimistic
/// concurrency; mismatch returns 409.
/// </remarks>
public sealed record UpdateTodoRequest(
    string Title,
    string? Description,
    DateOnly? DueDate,
    Priority? Priority,
    string[] Tags,
    uint? RowVersion);

/// <summary>
/// Toggle completion. <c>RowVersion</c> mandatory; mismatch returns 409 (same
/// concurrency contract as PUT). Without that, complete + edit interleave silently.
/// </summary>
public sealed record CompleteTodoRequest(bool? IsCompleted, uint? RowVersion);

/// <summary>
/// Wire shape for a todo. <strong>UserId is intentionally absent</strong> — the
/// global query filter and ownership tests both lean on responses never leaking
/// user identifiers.
/// </summary>
public sealed record TodoResponse(
    Guid Id,
    string Title,
    string? Description,
    DateOnly? DueDate,
    Priority Priority,
    bool IsCompleted,
    DateTimeOffset? CompletedAt,
    string[] Tags,
    uint RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int Total,
    bool HasNext);
