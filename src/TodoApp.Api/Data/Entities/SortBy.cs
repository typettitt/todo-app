namespace TodoApp.Api.Data.Entities;

/// <summary>
/// Strongly-typed sort field for the todo list endpoint. Reject unknown wire values
/// at bind time — never accept a raw <see cref="string"/> sortBy.
/// </summary>
public enum SortBy
{
    CreatedAt = 0,
    DueDate = 1,
    Priority = 2,
    Title = 3,
}
