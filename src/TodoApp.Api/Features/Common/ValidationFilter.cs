using FluentValidation;

namespace TodoApp.Api.Features.Common;

/// <summary>
/// Endpoint filter that runs FluentValidation on the first <typeparamref name="T"/>
/// argument bound to the endpoint and short-circuits to a canonical 400 ProblemDetails
/// payload when validation fails.
/// </summary>
public sealed class ValidationFilter<T> : IEndpointFilter
    where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        var arg = context.Arguments.OfType<T>().FirstOrDefault();
        if (arg is null)
        {
            var pd = ProblemDetailsBuilder.Problem(
                StatusCodes.Status400BadRequest,
                "Invalid request body.",
                "Request body is missing or could not be parsed.",
                http);
            return pd.ToProblemResult();
        }

        var validator = http.RequestServices.GetService<IValidator<T>>();
        if (validator is null)
        {
            return await next(context).ConfigureAwait(false);
        }

        var result = await validator.ValidateAsync(arg, http.RequestAborted).ConfigureAwait(false);
        if (!result.IsValid)
        {
            return ProblemDetailsExtensions.ValidationProblemDetails(http, result.Errors);
        }

        return await next(context).ConfigureAwait(false);
    }
}
