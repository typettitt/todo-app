using TodoApp.Api.Data.Entities;

namespace TodoApp.Api.Features.Auth;

/// <summary>
/// Register payload. <c>Role</c> is intentionally absent — never accept it from
/// a request DTO. Extra JSON fields ride along but cannot influence the C# type.
/// </summary>
public sealed record RegisterRequest(string Email, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthUserResponse(Guid Id, string Email, Role Role);

/// <summary>
/// Uniform register acknowledgement. Both branches — "new account created"
/// AND "email already in use" — return this same shape with a 200 status,
/// erasing the response-body register enumeration oracle. The "did
/// registration actually succeed" question is answered out-of-band by the
/// presence of an auth cookie + a successful follow-up <c>/me</c> probe.
/// See docs/decisions.md.
/// </summary>
public sealed record RegisterAcknowledgement(string Status);
