using System;

namespace AttendanceSystem.Domain.Exceptions;

public class TimeServiceUnavailableException : Exception
{
    public string? Reason { get; }

    private const string DefaultMessage =
        "External time service is unavailable. Clock-in/out operations require an accurate time source.";

    public TimeServiceUnavailableException()
        : base(DefaultMessage)
    {
    }

    public TimeServiceUnavailableException(string? reason)
        : base(DefaultMessage)
    {
        Reason = reason;
    }

    public TimeServiceUnavailableException(string? reason, Exception innerException)
        : base(DefaultMessage, innerException)
    {
        Reason = reason;
    }
}
