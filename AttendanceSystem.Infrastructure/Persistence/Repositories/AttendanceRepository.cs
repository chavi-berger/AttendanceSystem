using AttendanceSystem.Application.Common.Interfaces;
using AttendanceSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class AttendanceRepository : IAttendanceRepository
{
    private readonly AppDbContext _context;

    public AttendanceRepository(AppDbContext context) => _context = context;

    // Uses the filtered index IX_AttendanceLogs_OpenSessions (ClockOutUtc IS NULL).
    // Tracked (not AsNoTracking) because the caller typically mutates and saves it.
    public Task<AttendanceLog?> GetActiveSessionAsync(Guid employeeId, CancellationToken ct = default) =>
        _context.AttendanceLogs
            .Where(a => a.EmployeeId == employeeId && a.ClockOutUtc == null)
            .OrderByDescending(a => a.ClockInUtc)
            .FirstOrDefaultAsync(ct);

    public Task<AttendanceLog?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _context.AttendanceLogs.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IEnumerable<AttendanceLog>> GetHistoryAsync(
        Guid employeeId, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken ct = default)
    {
        var query = BuildHistoryQuery(employeeId, from, to);

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        return await query
            .OrderByDescending(a => a.ClockInUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public Task<int> GetHistoryCountAsync(
        Guid employeeId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default) =>
        BuildHistoryQuery(employeeId, from, to).CountAsync(ct);

    public async Task<IEnumerable<AttendanceLog>> GetAllActiveSessionsAsync(CancellationToken ct = default) =>
        await _context.AttendanceLogs
            .Where(a => a.ClockOutUtc == null)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<IEnumerable<AttendanceLog>> GetOpenSessionsOlderThanAsync(
        DateTimeOffset threshold, CancellationToken ct = default) =>
        await _context.AttendanceLogs
            .Where(a => a.ClockOutUtc == null && a.ClockInUtc < threshold)
            .ToListAsync(ct); // tracked: auto-timeout job mutates these

    public async Task AddAsync(AttendanceLog log, CancellationToken ct = default) =>
        await _context.AttendanceLogs.AddAsync(log, ct);

    public void Update(AttendanceLog log) => _context.AttendanceLogs.Update(log);

    private IQueryable<AttendanceLog> BuildHistoryQuery(Guid employeeId, DateTimeOffset? from, DateTimeOffset? to)
    {
        var query = _context.AttendanceLogs.Where(a => a.EmployeeId == employeeId);
        if (from.HasValue) query = query.Where(a => a.ClockInUtc >= from.Value);
        if (to.HasValue) query = query.Where(a => a.ClockInUtc <= to.Value);
        return query;
    }
}
