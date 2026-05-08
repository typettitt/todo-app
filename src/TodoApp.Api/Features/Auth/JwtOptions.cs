namespace TodoApp.Api.Features.Auth;

/// <summary>
/// Auth configuration bound from the <c>Jwt</c> configuration section. The signing
/// key is resolved separately by <see cref="JwtKeyProvider"/> — never read it from
/// configuration so we cannot accidentally commit a key.
/// </summary>
public sealed class JwtOptions
{
    public string Issuer { get; set; } = "todoapp";

    public string Audience { get; set; } = "todoapp";

    /// <summary>
    /// Total token lifetime. Default 30 minutes per spec.
    /// </summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Renew the cookie when remaining lifetime drops below
    /// <see cref="RenewalThreshold"/> × <see cref="Lifetime"/>.
    /// </summary>
    public double RenewalThreshold { get; set; } = 0.5;

    /// <summary>
    /// Minimum gap between two server-issued renewals for the same <c>sub</c>.
    /// Prevents the parallel-request stampede that produces flicker logout.
    /// </summary>
    public TimeSpan RenewalThrottle { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Cookie name. Spec calls for <c>auth</c>; the JwtBearer middleware reads
    /// the same cookie name in the configured <c>OnMessageReceived</c> event.
    /// </summary>
    public string CookieName { get; set; } = "auth";

    /// <summary>
    /// SameSite policy applied in non-Development environments. Lax is used in
    /// Development for compatibility with the Vite dev server over plain HTTP.
    /// </summary>
    public Microsoft.AspNetCore.Http.SameSiteMode CookieSameSite { get; set; } = Microsoft.AspNetCore.Http.SameSiteMode.Strict;
}
