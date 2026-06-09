using AttendanceSystem.Domain.Entities;

namespace AttendanceSystem.Application.Common.Interfaces;

public interface IEmployeeRepository
{
    Task<Employee?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Employee?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<Employee?> GetByBadgeNumberAsync(string badgeNumber, CancellationToken ct = default);
    Task<IEnumerable<Employee>> GetAllActiveAsync(CancellationToken ct = default);
    Task AddAsync(Employee employee, CancellationToken ct = default);
    void Update(Employee employee);
}
