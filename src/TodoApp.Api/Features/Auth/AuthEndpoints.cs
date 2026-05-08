using System.Security.Claims;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using TodoApp.Api.Data;
using TodoApp.Api.Data.Entities;
using TodoApp.Api.Features.Common;

namespace TodoApp.Api.Features.Auth;

public static class AuthEndpoints
{
    public const string AuthRateLimitPolicy = "auth";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/auth");

        // Rate limit stays on register even though both branches return the
        // same body — the limiter still protects the database from a flood of
        // insert attempts and the dummy/real PBKDF2 cost difference. DO NOT
        // strip RequireRateLimiting thinking the equalized response makes it
        // unnecessary. See docs/decisions.md.
        group.MapPost("/register", RegisterAsync)
            .AddEndpointFilter<ValidationFilter<RegisterRequest>>()
            .RequireRateLimiting(AuthRateLimitPolicy)
            .Produces<RegisterAcknowledgement>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AllowAnonymous();

        group.MapPost("/login", LoginAsync)
            .AddEndpointFilter<ValidationFilter<LoginRequest>>()
            .RequireRateLimiting(AuthRateLimitPolicy)
            .Produces<AuthUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AllowAnonymous();

        group.MapPost("/logout", Logout)
            .Produces(StatusCodes.Status204NoContent)
            .AllowAnonymous();

        group.MapGet("/me", MeAsync)
            .Produces<AuthUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        return routes;
    }

    /// <summary>
    /// Both register branches return the same response shape: 200 OK with
    /// <c>{ "status": "received" }</c>. New accounts additionally receive an
    /// auth cookie; duplicate emails do not. Do not add the user object back to
    /// this response because that would reintroduce an enumeration oracle.
    /// </summary>
    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest request,
        AuthService authService,
        AuthSessionService sessions,
        JwtTokenService tokens,
        AuthCookies cookies,
        IOptions<JwtOptions> options,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var ack = new RegisterAcknowledgement("received");

        try
        {
            var user = await authService.RegisterAsync(request.Email, request.Password, cancellationToken).ConfigureAwait(false);
            await IssueCookieAsync(user, http, tokens, sessions, cookies, options.Value, cancellationToken).ConfigureAwait(false);
            return Results.Ok(ack);
        }
        catch (EmailAlreadyExistsException)
        {
            // Same status, same body, no Set-Cookie. The duplicate branch is
            // indistinguishable from success at the body-shape level.
            return Results.Ok(ack);
        }
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        AuthService authService,
        LoginEmailRateLimiter emailLimiter,
        AuthSessionService sessions,
        JwtTokenService tokens,
        AuthCookies cookies,
        IOptions<JwtOptions> options,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var normalized = AuthService.NormalizeEmail(request.Email);
        if (!emailLimiter.TryAcquire(normalized))
        {
            return Results.Problem(ProblemDetailsBuilder.Problem(
                StatusCodes.Status429TooManyRequests,
                "Too many requests.",
                "Rate limit exceeded; please retry later.",
                http));
        }

        try
        {
            var user = await authService.LoginAsync(request.Email, request.Password, cancellationToken).ConfigureAwait(false);
            await IssueCookieAsync(user, http, tokens, sessions, cookies, options.Value, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new AuthUserResponse(user.Id, user.Email, user.Role));
        }
        catch (InvalidCredentialsException)
        {
            return Results.Problem(ProblemDetailsBuilder.Problem(
                StatusCodes.Status401Unauthorized,
                "Invalid credentials.",
                "Email or password is incorrect.",
                http));
        }
    }

    private static async Task<IResult> Logout(
        AuthCookies cookies,
        AuthSessionService sessions,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        // Always 204 — logout is idempotent and never advertises whether a real
        // session was cleared. If the request carries a valid principal with a
        // sid claim, revoke the row first so subsequent replays of the same
        // cookie fail OnTokenValidated. Authentication middleware ran before
        // endpoints, so HttpContext.User is the validated principal here (or
        // anonymous when there's no cookie / it was already invalid).
        var sidClaim = http.User.FindFirstValue("sid");
        if (!string.IsNullOrEmpty(sidClaim) && Guid.TryParse(sidClaim, out var sid))
        {
            await sessions.RevokeAsync(sid, cancellationToken).ConfigureAwait(false);
        }

        cookies.Clear(http);
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(
        ICurrentUser currentUser,
        TodoDbContext db,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);

        var id = currentUser.Id;
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return user is null
            ? ProblemDetailsExtensions.UnauthorizedProblem(http)
            : Results.Ok(new AuthUserResponse(user.Id, user.Email, user.Role));
    }

    private static async Task IssueCookieAsync(
        User user,
        HttpContext http,
        JwtTokenService tokens,
        AuthSessionService sessions,
        AuthCookies cookies,
        JwtOptions options,
        CancellationToken cancellationToken)
    {
        var session = await sessions.CreateAsync(user.Id, cancellationToken).ConfigureAwait(false);
        var issuedAt = DateTimeOffset.UtcNow;
        var token = tokens.IssueToken(user, session.Sid, issuedAt);
        cookies.Set(http, token, issuedAt.Add(options.Lifetime));
    }
}
