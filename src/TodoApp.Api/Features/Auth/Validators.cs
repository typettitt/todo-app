using FluentValidation;

namespace TodoApp.Api.Features.Auth;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .Must(e => !string.IsNullOrWhiteSpace(e)).WithMessage("Email cannot be whitespace.")
            .EmailAddress().WithMessage("Email must be a valid email address.")
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .Must(p => !string.IsNullOrWhiteSpace(p)).WithMessage("Password cannot be whitespace.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .MaximumLength(256);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .Must(e => !string.IsNullOrWhiteSpace(e)).WithMessage("Email cannot be whitespace.")
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}
