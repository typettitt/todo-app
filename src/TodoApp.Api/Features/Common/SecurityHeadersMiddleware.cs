namespace TodoApp.Api.Features.Common;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string ContentTypeOptions = "X-Content-Type-Options";
    private const string FrameOptions = "X-Frame-Options";
    private const string ReferrerPolicy = "Referrer-Policy";
    private const string ContentSecurityPolicy = "Content-Security-Policy";
    private const string PermissionsPolicy = "Permissions-Policy";
    private const string ApiContentSecurityPolicy =
        "default-src 'none'; object-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
    private const string ApiPermissionsPolicy = "camera=(), microphone=(), geolocation=()";

    private readonly RequestDelegate _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(static state =>
        {
            var httpContext = (HttpContext)state;
            httpContext.Response.Headers[ContentTypeOptions] = "nosniff";
            httpContext.Response.Headers[FrameOptions] = "DENY";
            httpContext.Response.Headers[ReferrerPolicy] = "no-referrer";
            httpContext.Response.Headers[PermissionsPolicy] = ApiPermissionsPolicy;

            var contentType = httpContext.Response.ContentType;
            if (contentType is null
                || !contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            {
                httpContext.Response.Headers[ContentSecurityPolicy] = ApiContentSecurityPolicy;
            }

            return Task.CompletedTask;
        }, context);

        await _next(context).ConfigureAwait(false);
    }
}
