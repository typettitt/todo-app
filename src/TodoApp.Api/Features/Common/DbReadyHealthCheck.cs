using Microsoft.Extensions.Diagnostics.HealthChecks;

using TodoApp.Api.Data;

namespace TodoApp.Api.Features.Common;

/// <summary>
/// Database readiness probe. Uses <see cref="MaintenanceDbContext"/> so the
/// infrastructure check has no <see cref="ICurrentUser"/> dependency — the probe
/// runs anonymously and only asserts <c>CanConnectAsync</c>.
/// </summary>
internal sealed class DbReadyHealthCheck(MaintenanceDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var canConnect = await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
        return canConnect
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Database connection failed.");
    }
}
