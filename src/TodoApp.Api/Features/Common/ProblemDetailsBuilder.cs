using System.Diagnostics;

using FluentValidation.Results;

using Microsoft.AspNetCore.Mvc;

namespace TodoApp.Api.Features.Common;

/// <summary>
/// Canonical RFC 7807 envelope used by every error path in the API. Endpoint
/// filters, auth handlers, and exception handlers all route through this shape
/// rather than hand-crafting incompatible error payloads.
/// </summary>
/// <remarks>
/// The wire shape is <c>{ type, title, status, detail?, instance?, traceId, errors? }</c>.
/// <c>traceId</c> is the active <see cref="Activity"/>'s <c>TraceId</c> when
/// available so a FE-visible trace id is greppable in Serilog output. ASP.NET
/// serializes <see cref="ProblemDetails.Extensions"/> as top-level JSON extension
/// data; do not introduce a nested <c>extensions</c> member or shadow these keys.
/// </remarks>
public static class ProblemDetailsBuilder
{
    public const string DefaultType = "about:blank";
    public const string ContentType = "application/problem+json";
    public const string TraceIdKey = "traceId";
    public const string ErrorsKey = "errors";
    public const string ConcurrencyRowVersionMessage =
        "The resource has changed since you loaded it. Reload and try again.";

    public static ProblemDetails Problem(int status, string title, string? detail, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var pd = new ProblemDetails
        {
            Type = DefaultType,
            Title = title,
            Status = status,
            Detail = detail,
            Instance = httpContext.Request.Path,
        };
        pd.Extensions[TraceIdKey] = ResolveTraceId(httpContext);
        return pd;
    }

    public static ProblemDetails Validation(IEnumerable<ValidationFailure> failures, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentNullException.ThrowIfNull(httpContext);

        var errors = failures
            .GroupBy(f => string.IsNullOrWhiteSpace(f.PropertyName) ? "_" : ToJsonPropertyName(f.PropertyName))
            .ToDictionary(
                g => g.Key,
                g => g.Select(f => f.ErrorMessage).ToArray(),
                StringComparer.Ordinal);

        var pd = new ProblemDetails
        {
            Type = DefaultType,
            Title = "One or more validation errors occurred.",
            Status = StatusCodes.Status400BadRequest,
            Instance = httpContext.Request.Path,
        };
        pd.Extensions[TraceIdKey] = ResolveTraceId(httpContext);
        pd.Extensions[ErrorsKey] = errors;
        return pd;
    }

    public static ProblemDetails Validation(
        IDictionary<string, string[]> errors,
        HttpContext httpContext,
        string? title = null)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(httpContext);

        var pd = new ProblemDetails
        {
            Type = DefaultType,
            Title = title ?? "One or more validation errors occurred.",
            Status = StatusCodes.Status400BadRequest,
            Instance = httpContext.Request.Path,
        };
        pd.Extensions[TraceIdKey] = ResolveTraceId(httpContext);
        pd.Extensions[ErrorsKey] = errors;
        return pd;
    }

    public static string ResolveTraceId(HttpContext httpContext) =>
        Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;

    public static string ToJsonPropertyName(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
        {
            return name;
        }

        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
