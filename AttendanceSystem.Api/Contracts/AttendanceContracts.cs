namespace AttendanceSystem.Api.Contracts;

public record ClockRequest(string? Notes = null, Guid? EmployeeId = null);

public record ManualCorrectionRequest(
    DateTimeOffset? NewClockIn,
    DateTimeOffset? NewClockOut,
    string Reason);
