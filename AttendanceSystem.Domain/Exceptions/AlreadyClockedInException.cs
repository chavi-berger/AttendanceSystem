using System;

namespace AttendanceSystem.Domain.Exceptions;

public class AlreadyClockedInException : Exception
{
    public Guid EmployeeId { get; }
    public DateTimeOffset ExistingClockInTime { get; }

    public AlreadyClockedInException(Guid employeeId, DateTimeOffset existingClockInTime)
        : base($"Employee {employeeId} already has an active clock-in session started at {existingClockInTime:O}")
    {
        EmployeeId = employeeId;
        ExistingClockInTime = existingClockInTime;
    }
}
