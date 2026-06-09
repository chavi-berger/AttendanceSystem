namespace AttendanceSystem.Application.Features.Attendance.Queries.GetHistory;

public class AttendanceHistoryDto
{
    public Guid Id { get; init; }
    public DateTimeOffset ClockInUtc { get; init; }
    public DateTimeOffset? ClockOutUtc { get; init; }
    public string? DurationFormatted { get; init; }  // "7h 43m", "7h 45m (crosses midnight)", or "In progress"
    public bool CrossesMidnight { get; init; }
    public bool IsOpen { get; init; }
    public bool IsManualCorrection { get; init; }
    public bool IsAutoTimeout { get; init; }
    public string ClockInSource { get; init; } = string.Empty;
}
