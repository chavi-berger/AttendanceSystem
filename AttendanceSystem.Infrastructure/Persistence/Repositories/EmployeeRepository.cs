using AttendanceSystem.Application.Common.Interfaces;
using AttendanceSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence.Repositories;

public class EmployeeRepository : IEmployeeRepository
{
    private readonly AppDbContext _context;

    public EmployeeRepository(AppDbContext context) => _context = context;

    // GetById is tracked so callers can mutate (e.g. refresh-token updates) and save.
    public Task<Employee?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _context.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<Employee?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return _context.Employees.FirstOrDefaultAsync(e => e.Email == normalized, ct);
    }

    public Task<Employee?> GetByBadgeNumberAsync(string badgeNumber, CancellationToken ct = default)
    {
        var normalized = badgeNumber.Trim().ToUpperInvariant();
        return _context.Employees.FirstOrDefaultAsync(e => e.BadgeNumber == normalized, ct);
    }

    public async Task<IEnumerable<Employee>> GetAllActiveAsync(CancellationToken ct = default) =>
        await _context.Employees
            .AsNoTracking()
            .OrderBy(e => e.FullName)
            .ToListAsync(ct); // global query filter already restricts to IsActive == true

    public async Task<IEnumerable<Employee>> GetAllAsync(CancellationToken ct = default) =>
        await _context.Employees
            .IgnoreQueryFilters() // include deactivated employees for admin management
            .AsNoTracking()
            .OrderBy(e => e.FullName)
            .ToListAsync(ct);

    public Task<Employee?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default) =>
        _context.Employees
            .IgnoreQueryFilters() // allow loading a deactivated employee (e.g. to reactivate)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task AddAsync(Employee employee, CancellationToken ct = default) =>
        await _context.Employees.AddAsync(employee, ct);

    public void Update(Employee employee) => _context.Employees.Update(employee);
}
