using AttendanceSystem.Domain.Entities;

namespace AttendanceSystem.Application.Common.Interfaces;

public interface IAuditRepository
{
    Task AddAsync(AuditLog log, CancellationToken ct = default);
    Task<IEnumerable<AuditLog>> GetByEntityAsync(string entityType, Guid entityId, CancellationToken ct = default);
}
