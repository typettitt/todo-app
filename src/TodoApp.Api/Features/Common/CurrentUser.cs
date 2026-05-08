using System.Security.Claims;

namespace TodoApp.Api.Features.Common;

/// <summary>
/// The single canonical source of "who is making this request." Production code
/// must never reach into <c>HttpContext.User</c> directly — read identity from
/// here so the EF global query filter and any authorization rule are anchored
/// on the same surface.
/// </summary>
/// <remarks>
/// <see cref="IsAuthenticated"/> is always safe to call. <see cref="Id"/> and
/// <see cref="Email"/> throw <see cref="InvalidOperationException"/> if there
/// is no authenticated principal. The TodoDbContext global filter routes
/// through a private helper that returns <see cref="Guid.Empty"/> when
/// unauthenticated, so the filter is fail-CLOSED (anonymous reads see zero
/// rows) without ever touching <see cref="Id"/>. Non-request paths that
/// legitimately need an unfiltered view (migrations, seeding, health probes)
/// use <c>MaintenanceDbContext</c> instead — it has no
/// <see cref="ICurrentUser"/> dependency at all.
/// </remarks>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid Id { get; }

    string Email { get; }
}

internal sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public Guid Id
    {
        get
        {
            EnsureAuthenticated();
            var sub = accessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? accessor.HttpContext!.User.FindFirstValue("sub");
            if (sub is null)
            {
                throw new InvalidOperationException("Authenticated principal is missing the subject claim.");
            }

            return Guid.Parse(sub, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public string Email
    {
        get
        {
            EnsureAuthenticated();
            return accessor.HttpContext!.User.FindFirstValue(ClaimTypes.Email)
                   ?? accessor.HttpContext!.User.FindFirstValue("email")
                   ?? throw new InvalidOperationException("Authenticated principal is missing the email claim.");
        }
    }

    private void EnsureAuthenticated()
    {
        if (!IsAuthenticated)
        {
            throw new InvalidOperationException("Not authenticated.");
        }
    }
}
