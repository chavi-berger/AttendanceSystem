using System;

namespace AttendanceSystem.Domain.Exceptions;

public class NotClockedInException : Exception
{
    public Guid EmployeeId { get; }

    public NotClockedInException(Guid employeeId)
        : base($"Employee {employeeId} does not have an active clock-in session")
    {
        EmployeeId = employeeId;
    }
}
