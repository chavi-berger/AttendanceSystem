using System;

namespace AttendanceSystem.Domain.Exceptions;

/// <summary>
/// Raised when an attendance operation violates a business rule
/// (e.g. shift too long, insufficient break between shifts).
/// </summary>
public class InvalidShiftException : Exception
{
    public string RuleViolated { get; }

    public InvalidShiftException(string ruleViolated, string message)
        : base(message)
    {
        RuleViolated = ruleViolated;
    }

    public InvalidShiftException(string ruleViolated, string message, Exception innerException)
        : base(message, innerException)
    {
        RuleViolated = ruleViolated;
    }
}
