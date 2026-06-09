using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Common.Behaviors;

/// <summary>
/// Logs each request: name, employee id (when present), and the elapsed duration plus outcome.
/// Uses a monotonic Stopwatch for timing (NOT wall-clock time).
/// </summary>
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger) => _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var employeeId = TryGetEmployeeId(request);

        _logger.LogInformation("Handling {RequestName} {EmployeeId}", requestName,
            employeeId is null ? string.Empty : $"(EmployeeId: {employeeId})");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next();
            stopwatch.Stop();
            _logger.LogInformation("Handled {RequestName} successfully in {ElapsedMs}ms",
                requestName, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "{RequestName} failed after {ElapsedMs}ms: {Message}",
                requestName, stopwatch.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    private static Guid? TryGetEmployeeId(TRequest request)
    {
        var prop = typeof(TRequest).GetProperty("EmployeeId");
        if (prop is not null && prop.PropertyType == typeof(Guid) && prop.GetValue(request) is Guid id)
            return id;
        return null;
    }
}
