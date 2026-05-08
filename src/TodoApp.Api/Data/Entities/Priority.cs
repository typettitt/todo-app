namespace TodoApp.Api.Data.Entities;

/// <summary>
/// Todo priority. Stored as text in SQLite via a string conversion for readability
/// and migration safety (numeric ordinals can be reordered by mistake).
/// </summary>
public enum Priority
{
    Low = 0,
    Medium = 1,
    High = 2,
}
