namespace AttendanceSystem.Application.Features.Attendance.Queries.GetActiveEmployees;

public class ActiveEmployeeDto
{
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string BadgeNumber { get; init; } = string.Empty;
    public DateTimeOffset ClockInUtc { get; init; }
    public string DurationSoFar { get; init; } = string.Empty;  // "3h 22m"
    public Guid LogId { get; init; }
}
