using FluentValidation.Results;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace TodoApp.Api.Features.Common;

/// <summary>
/// Thin result helpers for the canonical error envelope. Keep <c>traceId</c> and
/// <c>errors</c> as top-level ProblemDetails extension data; never add a nested
/// <c>extensions</c> object that would change the API contract.
/// </summary>
public static class ProblemDetailsExtensions
{
    public static IResult ToProblemResult(this ProblemDetails problemDetails)
    {
        ArgumentNullException.ThrowIfNull(problemDetails);

        return Results.Json(
            problemDetails,
            statusCode: problemDetails.Status ?? StatusCodes.Status500InternalServerError,
            contentType: ProblemDetailsBuilder.ContentType);
    }

    public static Task WriteProblemDetailsAsync(
        this HttpResponse response,
        ProblemDetails problemDetails,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(problemDetails);

        response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;
        response.ContentType = ProblemDetailsBuilder.ContentType;
        return response.WriteAsJsonAsync(
            problemDetails,
            options: null,
            contentType: ProblemDetailsBuilder.ContentType,
            cancellationToken: cancellationToken);
    }

    public static IResult ValidationProblemDetails(
        HttpContext httpContext,
        IEnumerable<ValidationFailure> failures)
    {
        var problemDetails = ProblemDetailsBuilder.Validation(failures, httpContext);
        return problemDetails.ToProblemResult();
    }

    public static IResult ValidationProblemDetails(
        HttpContext httpContext,
        IDictionary<string, string[]> errors,
        string? title = null)
    {
        var problemDetails = ProblemDetailsBuilder.Validation(errors, httpContext, title);
        return problemDetails.ToProblemResult();
    }

    public static IResult UnauthorizedProblem(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var problemDetails = ProblemDetailsBuilder.Problem(
            StatusCodes.Status401Unauthorized,
            "Unauthorized.",
            "Authentication is required.",
            httpContext);
        return problemDetails.ToProblemResult();
    }

    public static IResult NotFoundProblem(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var problemDetails = ProblemDetailsBuilder.Problem(
            StatusCodes.Status404NotFound,
            "Not found.",
            "The requested resource was not found.",
            httpContext);
        return problemDetails.ToProblemResult();
    }

    public static ProblemDetails ConcurrencyProblemDetails(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["rowVersion"] = new[] { ProblemDetailsBuilder.ConcurrencyRowVersionMessage },
        };
        var problemDetails = ProblemDetailsBuilder.Validation(errors, httpContext, "Concurrency conflict.");
        problemDetails.Status = StatusCodes.Status409Conflict;
        return problemDetails;
    }

    public static IResult ConflictProblem(HttpContext httpContext)
    {
        var problemDetails = ConcurrencyProblemDetails(httpContext);
        return problemDetails.ToProblemResult();
    }

    public static void NormalizeFrameworkProblemDetails(
        ProblemDetails problemDetails,
        HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(problemDetails);
        ArgumentNullException.ThrowIfNull(httpContext);

        Dictionary<string, string[]>? extensionErrors = ExtractAndNormalizeErrors(problemDetails);

        problemDetails.Extensions.Clear();

        var status = problemDetails.Status ?? httpContext.Response.StatusCode;
        if (status < StatusCodes.Status400BadRequest)
        {
            status = StatusCodes.Status500InternalServerError;
        }

        problemDetails.Type = ProblemDetailsBuilder.DefaultType;
        problemDetails.Status = status;
        problemDetails.Title = string.IsNullOrWhiteSpace(problemDetails.Title)
            ? ReasonPhrases.GetReasonPhrase(status)
            : problemDetails.Title;
        problemDetails.Instance = httpContext.Request.Path;
        problemDetails.Extensions[ProblemDetailsBuilder.TraceIdKey] =
            ProblemDetailsBuilder.ResolveTraceId(httpContext);

        if (extensionErrors is not null)
        {
            problemDetails.Extensions[ProblemDetailsBuilder.ErrorsKey] = extensionErrors;
        }
    }

    private static Dictionary<string, string[]>? ExtractAndNormalizeErrors(ProblemDetails problemDetails)
    {
        if (problemDetails is HttpValidationProblemDetails httpValidationProblemDetails)
        {
            NormalizeErrorsInPlace(httpValidationProblemDetails.Errors);
            return null;
        }

        if (problemDetails is ValidationProblemDetails validationProblemDetails)
        {
            NormalizeErrorsInPlace(validationProblemDetails.Errors);
            return null;
        }

        if (problemDetails.Extensions.TryGetValue(
            ProblemDetailsBuilder.ErrorsKey,
            out var rawErrors)
            && rawErrors is IDictionary<string, string[]> errors)
        {
            return NormalizeErrors(errors);
        }

        return null;
    }

    private static void NormalizeErrorsInPlace(IDictionary<string, string[]> errors)
    {
        if (errors.Count == 0)
        {
            return;
        }

        var normalized = NormalizeErrors(errors);
        errors.Clear();
        foreach (var error in normalized)
        {
            errors[error.Key] = error.Value;
        }
    }

    private static Dictionary<string, string[]> NormalizeErrors(IDictionary<string, string[]> errors)
    {
        return errors
            .GroupBy(
                e => string.IsNullOrWhiteSpace(e.Key) ? "_" : ProblemDetailsBuilder.ToJsonPropertyName(e.Key),
                StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(e => e.Value).ToArray(),
                StringComparer.Ordinal);
    }
}
