using AttendanceSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceSystem.Infrastructure.Persistence.Configurations;

public class AttendanceLogConfiguration : IEntityTypeConfiguration<AttendanceLog>
{
    public void Configure(EntityTypeBuilder<AttendanceLog> builder)
    {
        builder.ToTable("AttendanceLogs", t =>
            t.HasCheckConstraint(
                "CK_ClockOut_After_ClockIn",
                "[ClockOutUtc] IS NULL OR [ClockOutUtc] > [ClockInUtc]"));

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

        builder.Property(a => a.EmployeeId).IsRequired();

        builder.Property(a => a.ClockInUtc).IsRequired().HasColumnType("datetimeoffset(7)");
        builder.Property(a => a.ClockOutUtc).HasColumnType("datetimeoffset(7)");

        builder.Property(a => a.ClockInSource).IsRequired().HasMaxLength(200);
        builder.Property(a => a.ClockOutSource).HasMaxLength(200);
        builder.Property(a => a.CorrectionNotes).HasMaxLength(500);

        builder.Property(a => a.CreatedAt).HasColumnType("datetimeoffset(7)");
        builder.Property(a => a.LastModifiedAt).HasColumnType("datetimeoffset(7)");

        // Computed / non-persisted members
        builder.Ignore(a => a.Duration);
        builder.Ignore(a => a.IsOpen);

        // Fast history queries
        builder.HasIndex(a => new { a.EmployeeId, a.ClockInUtc });

        // Filtered index for finding open sessions quickly
        builder.HasIndex(a => a.ClockOutUtc)
               .HasFilter("[ClockOutUtc] IS NULL")
               .HasDatabaseName("IX_AttendanceLogs_OpenSessions");

        // Enforce AT MOST ONE open session per employee at the DB level.
        // This is the race-condition backstop for concurrent clock-in requests.
        builder.HasIndex(a => a.EmployeeId)
               .HasFilter("[ClockOutUtc] IS NULL")
               .IsUnique()
               .HasDatabaseName("UX_OneActiveSession");
    }
}
