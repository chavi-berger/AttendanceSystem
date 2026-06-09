using AttendanceSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(e => e.FullName).IsRequired().HasMaxLength(120);
        builder.Property(e => e.Email).IsRequired().HasMaxLength(256);
        builder.Property(e => e.BadgeNumber).IsRequired().HasMaxLength(30);
        builder.Property(e => e.PasswordHash).IsRequired().HasMaxLength(512);
        builder.Property(e => e.Role).IsRequired().HasMaxLength(20).HasDefaultValue("Employee");
        builder.Property(e => e.RefreshToken).HasMaxLength(512);
        builder.Property(e => e.CreatedAt).HasColumnType("datetimeoffset(7)");
        builder.Property(e => e.LastModifiedAt).HasColumnType("datetimeoffset(7)");
        builder.Property(e => e.RefreshTokenExpiry).HasColumnType("datetimeoffset(7)");

        // Unique constraints
        builder.HasIndex(e => e.Email).IsUnique();
        builder.HasIndex(e => e.BadgeNumber).IsUnique();

        // Relationship (backing field _attendanceLogs on the aggregate root)
        builder.HasMany(e => e.AttendanceLogs)
               .WithOne()
               .HasForeignKey(a => a.EmployeeId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
