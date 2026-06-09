using AttendanceSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd(); // IDENTITY(1,1)

        builder.Property(a => a.EntityType).IsRequired().HasMaxLength(50);
        builder.Property(a => a.EntityId).IsRequired();
        builder.Property(a => a.Action).IsRequired().HasMaxLength(30);

        builder.Property(a => a.OldValueJson).HasColumnType("nvarchar(max)");
        builder.Property(a => a.NewValueJson).HasColumnType("nvarchar(max)");

        builder.Property(a => a.IpAddress).HasMaxLength(45);
        builder.Property(a => a.UserAgent).HasMaxLength(500);

        builder.Property(a => a.ChangedAt).HasColumnType("datetimeoffset(7)");

        // Audit history of a specific record
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        // Time-based queries
        builder.HasIndex(a => a.ChangedAt);
    }
}
