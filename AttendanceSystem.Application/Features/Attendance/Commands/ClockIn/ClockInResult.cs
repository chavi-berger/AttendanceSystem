namespace AttendanceSystem.Application.Features.Attendance.Commands.ClockIn;

public class ClockInResult
{
    public Guid LogId { get; init; }
    public Guid EmployeeId { get; init; }
    public DateTimeOffset ClockInUtc { get; init; }
    public string TimeSource { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
}
