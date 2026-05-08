namespace TodoApp.Api.Data.Entities;

/// <summary>
/// Application-level role for a <see cref="User"/>. Server-set only; never accepted from a request DTO.
/// </summary>
public enum Role
{
    Basic = 0,
    Admin = 1,
}
