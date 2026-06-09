using System.Text.Json;
using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Common.Interfaces;
using AttendanceSystem.Domain.Entities;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ManualCorrection;

public class ManualCorrectionCommandHandler : IRequestHandler<ManualCorrectionCommand, ManualCorrectionResult>
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<ManualCorrectionCommandHandler> _logger;

    public ManualCorrectionCommandHandler(IUnitOfWork uow, ILogger<ManualCorrectionCommandHandler> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<ManualCorrectionResult> Handle(ManualCorrectionCommand request, CancellationToken ct)
    {
        // Step 1: Get the log
        var log = await _uow.Attendance.GetByIdAsync(request.LogId, ct)
            ?? throw new KeyNotFoundException($"Attendance log {request.LogId} was not found");

        // Step 2: Capture old values for the audit diff
        var oldValueJson = JsonSerializer.Serialize(new
        {
            log.ClockInUtc,
            log.ClockOutUtc,
            DurationFormatted = DurationFormatter.FormatOrInProgress(log.Duration)
        });

        // Step 3: Apply the correction (domain validates ordering + required notes)
        log.ApplyManualCorrection(request.NewClockIn, request.NewClockOut, request.CorrectedByEmployeeId, request.Reason);
        _uow.Attendance.Update(log);

        // Step 4: Detailed audit record (who/when/what changed)
        var newValueJson = JsonSerializer.Serialize(new
        {
            log.ClockInUtc,
            log.ClockOutUtc,
            DurationFormatted = DurationFormatter.FormatOrInProgress(log.Duration),
            log.IsManualCorrection,
            log.CorrectedByEmployeeId,
            log.CorrectionNotes
        });
        var audit = AuditLog.Create("AttendanceLog", log.Id, "ManualCorrection",
            oldValueJson, newValueJson, request.CorrectedByEmployeeId);
        await _uow.Audits.AddAsync(audit, ct);

        // Step 5: Save
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Attendance log {LogId} manually corrected by {CorrectedBy}: {Reason}",
            log.Id, request.CorrectedByEmployeeId, request.Reason);

        return new ManualCorrectionResult
        {
            LogId = log.Id,
            ClockInUtc = log.ClockInUtc,
            ClockOutUtc = log.ClockOutUtc,
            DurationFormatted = DurationFormatter.FormatOrInProgress(log.Duration),
            CorrectedByEmployeeId = request.CorrectedByEmployeeId
        };
    }
}
