namespace TodoApp.Api.Data.Entities;

/// <summary>
/// Query-string filter for the GET /api/todos list endpoint. <em>Not</em> a stored
/// field on <see cref="Todo"/> — completion status lives in <see cref="Todo.IsCompleted"/>
/// and the DueToday match is computed against the client-supplied <c>today</c> param
/// (the server never trusts <c>DateTime.UtcNow</c> for "today" — see plan.md DueToday
/// timezone fence-post).
/// </summary>
public enum TodoStatusFilter
{
    All = 0,
    Active = 1,
    Completed = 2,
    DueToday = 3,
}
