using FluentValidation;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace TodoApp.Api.Features.Common;

internal sealed class ConcurrencyExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is not DbUpdateConcurrencyException)
        {
            return false;
        }

        var problemDetails = ProblemDetailsExtensions.ConcurrencyProblemDetails(httpContext);
        await httpContext.Response
            .WriteProblemDetailsAsync(problemDetails, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}

internal sealed class ValidationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is not ValidationException validationException)
        {
            return false;
        }

        var problemDetails = ProblemDetailsBuilder.Validation(validationException.Errors, httpContext);
        await httpContext.Response
            .WriteProblemDetailsAsync(problemDetails, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}

internal sealed class FallbackExceptionHandler(ILogger<FallbackExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var traceId = ProblemDetailsBuilder.ResolveTraceId(httpContext);
        logger.UnhandledException(exception, traceId);

        var problemDetails = ProblemDetailsBuilder.Problem(
            StatusCodes.Status500InternalServerError,
            "An unexpected error occurred.",
            detail: null,
            httpContext);

        await httpContext.Response
            .WriteProblemDetailsAsync(problemDetails, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}
