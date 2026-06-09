using AttendanceSystem.Domain.Entities;

namespace AttendanceSystem.Application.Common.Interfaces;

public interface IEmployeeRepository
{
    Task<Employee?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Employee?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<Employee?> GetByBadgeNumberAsync(string badgeNumber, CancellationToken ct = default);
    Task<IEnumerable<Employee>> GetAllActiveAsync(CancellationToken ct = default);

    /// <summary>All employees, including deactivated ones (bypasses the IsActive query filter). For admin management.</summary>
    Task<IEnumerable<Employee>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Tracked fetch by id that also returns deactivated employees (e.g. to reactivate them).</summary>
    Task<Employee?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Employee employee, CancellationToken ct = default);
    void Update(Employee employee);
}
