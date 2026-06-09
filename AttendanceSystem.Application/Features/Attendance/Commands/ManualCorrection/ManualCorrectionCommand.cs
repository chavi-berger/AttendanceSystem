using MediatR;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ManualCorrection;

public record ManualCorrectionCommand(
    Guid LogId,
    Guid CorrectedByEmployeeId,
    DateTimeOffset? NewClockIn,
    DateTimeOffset? NewClockOut,
    string Reason
) : IRequest<ManualCorrectionResult>;

public class ManualCorrectionResult
{
    public Guid LogId { get; init; }
    public DateTimeOffset ClockInUtc { get; init; }
    public DateTimeOffset? ClockOutUtc { get; init; }
    public string? DurationFormatted { get; init; }
    public Guid CorrectedByEmployeeId { get; init; }
}
