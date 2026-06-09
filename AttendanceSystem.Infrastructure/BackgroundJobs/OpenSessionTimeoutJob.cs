using System.Text.Json;
using AttendanceSystem.Application.Common.Interfaces;
using AttendanceSystem.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Infrastructure.BackgroundJobs;

/// <summary>
/// Periodically closes attendance sessions that have stayed open longer than the configured
/// AutoTimeoutHours (e.g. someone forgot to clock out). Runs hourly.
/// </summary>
public class OpenSessionTimeoutJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OpenSessionTimeoutJob> _logger;
    private readonly IConfiguration _config;

    public OpenSessionTimeoutJob(
        IServiceScopeFactory scopeFactory,
        ILogger<OpenSessionTimeoutJob> logger,
        IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break; // graceful shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto-timeout job iteration failed; will retry next cycle.");
            }
        }
    }

    /// <summary>
    /// Closes all sessions open longer than AutoTimeoutHours. Public so it can be triggered
    /// directly (e.g. integration tests) without waiting for the hourly timer.
    /// Returns the number of sessions that were auto-closed.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var autoTimeoutHours = _config.GetValue("AttendanceRules:AutoTimeoutHours", 16);
        var threshold = DateTimeOffset.UtcNow.AddHours(-autoTimeoutHours);

        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var expiredSessions = (await uow.Attendance.GetOpenSessionsOlderThanAsync(threshold, ct)).ToList();

        foreach (var session in expiredSessions)
        {
            var oldJson = JsonSerializer.Serialize(new { session.ClockInUtc, session.ClockOutUtc, IsOpen = true });
            session.ApplyAutoTimeout(DateTimeOffset.UtcNow);
            uow.Attendance.Update(session);

            var audit = AuditLog.Create("AttendanceLog", session.Id, "AutoTimeout",
                oldJson,
                JsonSerializer.Serialize(new { session.ClockOutUtc, IsAutoTimeout = true }),
                null);
            await uow.Audits.AddAsync(audit, ct);

            _logger.LogWarning(
                "Auto-timeout applied to session {LogId} for employee {EmployeeId}. Session was open since {ClockIn}",
                session.Id, session.EmployeeId, session.ClockInUtc);
        }

        if (expiredSessions.Count > 0)
            await uow.SaveChangesAsync(ct);

        return expiredSessions.Count;
    }
}
