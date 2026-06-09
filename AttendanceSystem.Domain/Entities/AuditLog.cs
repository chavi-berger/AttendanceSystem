using System;

namespace AttendanceSystem.Domain.Entities;

public class AuditLog
{
    public long Id { get; private set; }
    public string EntityType { get; private set; } = string.Empty; // e.g. "AttendanceLog", "Employee"
    public Guid EntityId { get; private set; }
    public string Action { get; private set; } = string.Empty; // e.g. "ClockIn", "ClockOut", "ManualCorrection", "AutoTimeout", "Create", "Deactivate"
    public string? OldValueJson { get; private set; }
    public string? NewValueJson { get; private set; }
    public Guid? ChangedByEmployeeId { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }

    // EF Core constructor
    private AuditLog() { }

    public static AuditLog Create(
        string entityType,
        Guid entityId,
        string action,
        string? oldValueJson = null,
        string? newValueJson = null,
        Guid? changedByEmployeeId = null,
        string? ipAddress = null,
        string? userAgent = null)
    {
        if (string.IsNullOrWhiteSpace(entityType)) throw new ArgumentException("EntityType is required", nameof(entityType));
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("Action is required", nameof(action));

        return new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            OldValueJson = oldValueJson,
            NewValueJson = newValueJson,
            ChangedByEmployeeId = changedByEmployeeId,
            ChangedAt = DateTimeOffset.UtcNow,
            IpAddress = ipAddress,
            UserAgent = userAgent
        };
    }
}
