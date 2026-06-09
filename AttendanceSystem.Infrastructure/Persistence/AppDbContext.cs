using AttendanceSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<AttendanceLog> AttendanceLogs => Set<AttendanceLog>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Soft-delete: only active employees are returned by default.
        // Admin-facing queries can bypass this with IgnoreQueryFilters().
        modelBuilder.Entity<Employee>().HasQueryFilter(e => e.IsActive);

        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditTimestamps();
        return base.SaveChanges();
    }

    /// <summary>
    /// Auto-sets LastModifiedAt on every modified entity that exposes the property.
    /// The domain entities keep private setters, so we write through the change tracker.
    /// </summary>
    private void ApplyAuditTimestamps()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Modified) continue;

            var lastModified = entry.Metadata.FindProperty("LastModifiedAt");
            if (lastModified is not null)
            {
                entry.Property("LastModifiedAt").CurrentValue = DateTimeOffset.UtcNow;
            }
        }
    }
}
