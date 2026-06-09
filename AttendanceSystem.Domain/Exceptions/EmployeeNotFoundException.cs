using System;

namespace AttendanceSystem.Domain.Exceptions;

public class EmployeeNotFoundException : Exception
{
    public string IdentifierType { get; }
    public string Identifier { get; }

    public EmployeeNotFoundException(string identifierType, string identifier)
        : base($"Employee with {identifierType} '{identifier}' was not found")
    {
        IdentifierType = identifierType;
        Identifier = identifier;
    }
}
