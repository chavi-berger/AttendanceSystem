using AttendanceSystem.Infrastructure.ExternalServices.Time;

namespace AttendanceSystem.Api.Extensions;

public static class HealthCheckExtensions
{
    public static IServiceCollection AddHealthChecksConfiguration(this IServiceCollection services, IConfiguration config)
    {
        services.AddHealthChecks()
            .AddSqlServer(
                connectionString: config.GetConnectionString("DefaultConnection")!,
                name: "sql-server",
                tags: new[] { "db", "sql", "ready" })
            .AddUrlGroup(
                uri: new Uri("https://timeapi.io/api/Time/current/zone?timeZone=Europe/Zurich"),
                name: "time-api",
                tags: new[] { "external", "time-api", "ready" })
            .AddCheck<TimeServiceHealthCheck>(
                "time-service",
                tags: new[] { "external", "ready" });

        return services;
    }
}
