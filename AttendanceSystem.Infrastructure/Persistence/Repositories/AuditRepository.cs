using AttendanceSystem.Application.Common.Interfaces;
using AttendanceSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class AuditRepository : IAuditRepository
{
    private readonly AppDbContext _context;

    public AuditRepository(AppDbContext context) => _context = context;

    public async Task AddAsync(AuditLog log, CancellationToken ct = default) =>
        await _context.AuditLogs.AddAsync(log, ct);

    public async Task<IEnumerable<AuditLog>> GetByEntityAsync(string entityType, Guid entityId, CancellationToken ct = default) =>
        await _context.AuditLogs
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .OrderByDescending(a => a.ChangedAt)
            .AsNoTracking()
            .ToListAsync(ct);
}
