using FluentValidation;

namespace TodoApp.Api.Features.Todos;

public sealed class CreateTodoRequestValidator : AbstractValidator<CreateTodoRequest>
{
    public CreateTodoRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Title cannot be whitespace.")
            .MaximumLength(200).WithMessage("Title must be at most 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description must be at most 2000 characters.");

        RuleFor(x => x.Priority!.Value)
            .IsInEnum().WithMessage("Priority must be Low, Medium, or High.")
            .When(x => x.Priority.HasValue);

        RuleFor(x => x.Tags!)
            .Must(t => t.Length <= 20).WithMessage("Tags is limited to 20 entries.")
            .When(x => x.Tags is not null);

        RuleForEach(x => x.Tags!)
            .NotEmpty().WithMessage("Tag must not be empty.")
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Tag cannot be whitespace.")
            .MaximumLength(50).WithMessage("Tag must be at most 50 characters.")
            .When(x => x.Tags is not null);
    }
}

public sealed class UpdateTodoRequestValidator : AbstractValidator<UpdateTodoRequest>
{
    public UpdateTodoRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Title cannot be whitespace.")
            .MaximumLength(200).WithMessage("Title must be at most 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description must be at most 2000 characters. Omit to clear.");

        RuleFor(x => x.Priority)
            .NotNull().WithMessage("Priority is required.")
            .IsInEnum().WithMessage("Priority must be Low, Medium, or High.");

        RuleFor(x => x.RowVersion)
            .NotNull().WithMessage("RowVersion is required.");

        RuleFor(x => x.Tags)
            .NotNull().WithMessage("Tags is required (PUT is a full replace; send [] to clear).")
            .Must(t => t.Length <= 20).WithMessage("Tags is limited to 20 entries.")
            .When(x => x.Tags is not null);

        RuleForEach(x => x.Tags)
            .NotEmpty().WithMessage("Tag must not be empty.")
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Tag cannot be whitespace.")
            .MaximumLength(50).WithMessage("Tag must be at most 50 characters.")
            .When(x => x.Tags is not null);
    }
}

public sealed class CompleteTodoRequestValidator : AbstractValidator<CompleteTodoRequest>
{
    public CompleteTodoRequestValidator()
    {
        RuleFor(x => x.IsCompleted)
            .NotNull().WithMessage("IsCompleted is required.");

        RuleFor(x => x.RowVersion)
            .NotNull().WithMessage("RowVersion is required.");
    }
}
