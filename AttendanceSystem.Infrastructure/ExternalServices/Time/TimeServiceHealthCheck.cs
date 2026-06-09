using AttendanceSystem.Application.Common.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AttendanceSystem.Infrastructure.ExternalServices.Time;

/// <summary>
/// Health check for the external time service. Never throws — any failure is reported as Unhealthy.
/// </summary>
public class TimeServiceHealthCheck : IHealthCheck
{
    private readonly ITimeService _timeService;

    public TimeServiceHealthCheck(ITimeService timeService) => _timeService = timeService;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var healthy = await _timeService.IsHealthyAsync(cancellationToken);
            return healthy
                ? HealthCheckResult.Healthy("External time service is reachable.")
                : HealthCheckResult.Unhealthy("External time service is unavailable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("External time service health check failed.", ex);
        }
    }
}
