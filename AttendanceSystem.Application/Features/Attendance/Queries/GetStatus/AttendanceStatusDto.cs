namespace AttendanceSystem.Application.Features.Attendance.Queries.GetStatus;

public class AttendanceStatusDto
{
    public bool IsClockedIn { get; init; }
    public DateTimeOffset? ClockInTime { get; init; }
    public string? DurationSoFar { get; init; }
    public Guid? LogId { get; init; }

    /// <summary>Current Europe/Zurich time fetched from the external time service (null if unavailable).</summary>
    public DateTimeOffset? CurrentZurichTime { get; init; }
}
