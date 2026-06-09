using System.Text.Json;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Common.Interfaces;
using AttendanceSystem.Domain.Entities;
using AttendanceSystem.Domain.Exceptions;
using AttendanceSystem.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ClockOut;

public class ClockOutCommandHandler : IRequestHandler<ClockOutCommand, ClockOutResult>
{
    private readonly IUnitOfWork _uow;
    private readonly ITimeService _timeService;
    private readonly ILogger<ClockOutCommandHandler> _logger;

    public ClockOutCommandHandler(IUnitOfWork uow, ITimeService timeService, ILogger<ClockOutCommandHandler> logger)
    {
        _uow = uow;
        _timeService = timeService;
        _logger = logger;
    }

    public async Task<ClockOutResult> Handle(ClockOutCommand request, CancellationToken ct)
    {
        // Step 1: Verify employee exists and is active
        var employee = await _uow.Employees.GetByIdAsync(request.EmployeeId, ct)
            ?? throw new EmployeeNotFoundException("Id", request.EmployeeId.ToString());

        if (!employee.IsActive)
            throw new EmployeeNotFoundException("Id", request.EmployeeId.ToString());

        // Step 2: Find active session
        var session = await _uow.Attendance.GetActiveSessionAsync(request.EmployeeId, ct)
            ?? throw new NotClockedInException(request.EmployeeId);

        var oldValueJson = JsonSerializer.Serialize(new
        {
            session.ClockInUtc,
            ClockOutUtc = (DateTimeOffset?)null,
            Status = "Open"
        });

        // Step 3: Get official time from external API (same no-fallback contract as ClockIn)
        ZurichTime zurichTime;
        try
        {
            zurichTime = await _timeService.GetCurrentTimeAsync(ct);
        }
        catch (TimeServiceUnavailableException)
        {
            throw; // middleware maps to HTTP 503 — never substitute server time
        }

        // Step 4: Record the clock-out on the domain entity
        session.RecordClockOut(zurichTime.Value, zurichTime.Source);
        _uow.Attendance.Update(session);

        // Step 5: Audit (old = open session, new = closed session with duration)
        var newValueJson = JsonSerializer.Serialize(new
        {
            session.ClockInUtc,
            session.ClockOutUtc,
            session.ClockOutSource,
            DurationFormatted = DurationFormatter.FormatOrInProgress(session.Duration),
            Status = "Closed"
        });
        var audit = AuditLog.Create("AttendanceLog", session.Id, "ClockOut",
            oldValueJson, newValueJson, request.EmployeeId);
        await _uow.Audits.AddAsync(audit, ct);

        // Step 6: Save
        await _uow.SaveChangesAsync(ct);

        var durationFormatted = DurationFormatter.FormatOrInProgress(session.Duration);
        _logger.LogInformation("Employee {EmployeeId} ({Name}) clocked out at {Time}; duration {Duration}",
            employee.Id, employee.FullName, zurichTime.Value, durationFormatted);

        // Step 7: Return result
        return new ClockOutResult
        {
            LogId = session.Id,
            EmployeeId = employee.Id,
            ClockInUtc = session.ClockInUtc,
            ClockOutUtc = session.ClockOutUtc!.Value,
            DurationFormatted = durationFormatted
        };
    }
}
