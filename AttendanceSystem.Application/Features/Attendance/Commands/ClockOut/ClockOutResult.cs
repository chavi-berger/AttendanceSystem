namespace AttendanceSystem.Application.Features.Attendance.Commands.ClockOut;

public class ClockOutResult
{
    public Guid LogId { get; init; }
    public Guid EmployeeId { get; init; }
    public DateTimeOffset ClockInUtc { get; init; }
    public DateTimeOffset ClockOutUtc { get; init; }
    public string DurationFormatted { get; init; } = string.Empty; // "Xh Ym"
}
