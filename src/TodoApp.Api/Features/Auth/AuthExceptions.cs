namespace TodoApp.Api.Features.Auth;

#pragma warning disable CA1032 // Standard exception ctors not needed; surface is internal
#pragma warning disable CA1064 // Internal exception types are fine for this app

/// <summary>
/// Thrown by <see cref="AuthService.RegisterAsync"/> when an account with the same
/// (case-insensitive) email already exists. Mapped to 409 by the endpoint handler.
/// </summary>
internal sealed class EmailAlreadyExistsException(string email)
    : InvalidOperationException($"An account already exists for '{email}'.")
{
    public string Email { get; } = email;
}

/// <summary>
/// Thrown by <see cref="AuthService.LoginAsync"/> for any combination of unknown
/// email and wrong password. Always 401 — never expose which one mismatched.
/// </summary>
internal sealed class InvalidCredentialsException()
    : InvalidOperationException("Invalid email or password.");

#pragma warning restore CA1064
#pragma warning restore CA1032
