namespace AttendanceSystem.Application.Common.Interfaces;

public interface IUnitOfWork
{
    IAttendanceRepository Attendance { get; }
    IEmployeeRepository Employees { get; }
    IAuditRepository Audits { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task RollbackTransactionAsync(CancellationToken ct = default);
}
