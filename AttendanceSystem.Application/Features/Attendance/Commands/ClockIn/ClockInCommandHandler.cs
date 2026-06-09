using System.Text.Json;
using AttendanceSystem.Application.Common.Interfaces;
using AttendanceSystem.Domain.Entities;
using AttendanceSystem.Domain.Exceptions;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ClockIn;

public class ClockInCommandHandler : IRequestHandler<ClockInCommand, ClockInResult>
{
    private readonly IUnitOfWork _uow;
    private readonly ITimeService _timeService;
    private readonly ILogger<ClockInCommandHandler> _logger;

    public ClockInCommandHandler(IUnitOfWork uow, ITimeService timeService, ILogger<ClockInCommandHandler> logger)
    {
        _uow = uow;
        _timeService = timeService;
        _logger = logger;
    }

    public async Task<ClockInResult> Handle(ClockInCommand request, CancellationToken ct)
    {
        // Step 1: Verify employee exists and is active
        var employee = await _uow.Employees.GetByIdAsync(request.EmployeeId, ct)
            ?? throw new EmployeeNotFoundException("Id", request.EmployeeId.ToString());

        if (!employee.IsActive)
            throw new EmployeeNotFoundException("Id", request.EmployeeId.ToString());

        // Step 2: Check for existing open session (IDEMPOTENCY CHECK)
        var existingSession = await _uow.Attendance.GetActiveSessionAsync(request.EmployeeId, ct);
        if (existingSession != null)
            throw new AlreadyClockedInException(request.EmployeeId, existingSession.ClockInUtc);

        // Step 3: Get official time from external API (CRITICAL — no fallback to server clock)
        ZurichTime zurichTime;
        try
        {
            zurichTime = await _timeService.GetCurrentTimeAsync(ct);
        }
        catch (TimeServiceUnavailableException)
        {
            throw; // Re-throw, let middleware handle HTTP 503 — never substitute server time
        }

        // Step 4: Create attendance log
        var log = AttendanceLog.CreateClockIn(
            request.EmployeeId,
            zurichTime.Value,
            zurichTime.Source);

        // Step 5: Persist
        await _uow.Attendance.AddAsync(log, ct);

        // Step 6: Write audit log
        var audit = AuditLog.Create("AttendanceLog", log.Id, "ClockIn",
            null,
            JsonSerializer.Serialize(new { log.ClockInUtc, log.ClockInSource }),
            request.EmployeeId);
        await _uow.Audits.AddAsync(audit, ct);

        // Step 7: Save atomically
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Employee {EmployeeId} ({Name}) clocked in at {Time} via {Source}",
            employee.Id, employee.FullName, zurichTime.Value, zurichTime.Source);

        return new ClockInResult
        {
            LogId = log.Id,
            EmployeeId = employee.Id,
            ClockInUtc = log.ClockInUtc,
            TimeSource = zurichTime.Source,
            EmployeeName = employee.FullName
        };
    }
}
