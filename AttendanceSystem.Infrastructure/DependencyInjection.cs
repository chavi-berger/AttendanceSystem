using AttendanceSystem.Application.Common.Interfaces;
using AttendanceSystem.Infrastructure.BackgroundJobs;
using AttendanceSystem.Infrastructure.ExternalServices.Time;
using AttendanceSystem.Infrastructure.Persistence;
using AttendanceSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AttendanceSystem.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        // ---- Persistence ----
        var connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        services.AddScoped<IAttendanceRepository, AttendanceRepository>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // ---- External time service ----
        services.Configure<TimeApiSettings>(config.GetSection(TimeApiSettings.SectionName));
        var timeSettings = config.GetSection(TimeApiSettings.SectionName).Get<TimeApiSettings>() ?? new TimeApiSettings();

        if (config.GetValue<bool>("UseTimeMock"))
        {
            // Development/testing: deterministic time, still behind ITimeService (never the server clock).
            services.AddSingleton<ITimeService, MockTimeService>();
        }
        else
        {
            services.AddMemoryCache();
            services.AddTimeServiceWithPolly(timeSettings);
            services.AddScoped<ITimeService, CachedTimeService>();
        }

        // ---- Background jobs ----
        // Registered as a singleton too so it can be resolved/triggered directly (e.g. tests).
        services.AddSingleton<OpenSessionTimeoutJob>();
        services.AddHostedService(sp => sp.GetRequiredService<OpenSessionTimeoutJob>());

        return services;
    }
}
