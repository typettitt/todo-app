namespace TodoApp.Api.Data.Entities;

/// <summary>
/// Server-side session row backing every issued auth cookie. The JWT carries a
/// <c>sid</c> claim referencing this row; the JwtBearer validator rejects the
/// token if the row is missing, revoked, or expired.
/// </summary>
/// <remarks>
/// This is the invalidation surface that JWT alone does not provide. Logout
/// flips <see cref="RevokedAt"/>; the next request with the same cookie fails
/// validation. Sliding renewal extends <see cref="ExpiresAt"/> up to (but never
/// past) <see cref="AbsoluteExpiresAt"/>, so an actively-used session lives no
/// longer than the absolute cap from creation.
/// </remarks>
public class AuthSession
{
    public Guid Sid { get; set; }

    public Guid UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset AbsoluteExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}
