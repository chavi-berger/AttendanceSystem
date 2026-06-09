using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AttendanceSystem.Infrastructure.Persistence;

/// <summary>
/// Used by the EF Core CLI (`dotnet ef migrations` / `database update`) so it can create the
/// DbContext without booting the API host. Keeps migration tooling decoupled from Program.cs.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Connection string mirrors appsettings.json:DefaultConnection. Only used at design time.
        var connectionString = Environment.GetEnvironmentVariable("ATTENDANCE_DB_CONNECTION")
            ?? "Server=localhost,1433;Database=AttendanceDB;User Id=sa;Password=Attendance@Strong123!;TrustServerCertificate=True;";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql =>
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options);
    }
}
