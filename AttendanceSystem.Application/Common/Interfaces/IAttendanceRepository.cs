using AttendanceSystem.Domain.Entities;

namespace AttendanceSystem.Application.Common.Interfaces;

public interface IAttendanceRepository
{
    Task<AttendanceLog?> GetActiveSessionAsync(Guid employeeId, CancellationToken ct = default);
    Task<AttendanceLog?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<AttendanceLog>> GetHistoryAsync(Guid employeeId, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken ct = default);
    Task<int> GetHistoryCountAsync(Guid employeeId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);
    Task<IEnumerable<AttendanceLog>> GetAllActiveSessionsAsync(CancellationToken ct = default);
    Task<IEnumerable<AttendanceLog>> GetOpenSessionsOlderThanAsync(DateTimeOffset threshold, CancellationToken ct = default);
    Task AddAsync(AttendanceLog log, CancellationToken ct = default);
    void Update(AttendanceLog log);
}
