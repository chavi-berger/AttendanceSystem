using AttendanceSystem.Domain.Constants;
using AttendanceSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using BCryptNet = BCrypt.Net.BCrypt;

namespace AttendanceSystem.Infrastructure.Persistence;

public static class DbSeeder
{
    // Password for all seed users: "Test@1234"
    public static async Task SeedAsync(AppDbContext context, CancellationToken ct = default)
    {
        if (await context.Employees.AnyAsync(ct))
            return; // already seeded

        var employees = new[]
        {
            Employee.Create("Anna Müller",  "anna@company.ch",  "EMP001", BCryptNet.HashPassword("Test@1234"), Roles.Employee),
            Employee.Create("Hans Weber",   "hans@company.ch",  "EMP002", BCryptNet.HashPassword("Test@1234"), Roles.Manager),
            Employee.Create("Admin System", "admin@company.ch", "ADM001", BCryptNet.HashPassword("Test@1234"), Roles.Admin),
        };

        await context.Employees.AddRangeAsync(employees, ct);
        await context.SaveChangesAsync(ct);
    }
}
