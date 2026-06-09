using System;

namespace AttendanceSystem.Domain.Entities;

public class AttendanceLog
{
    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }

    public DateTimeOffset ClockInUtc { get; private set; }
    public DateTimeOffset? ClockOutUtc { get; private set; }

    /// <summary>The API URL that provided the clock-in time, e.g. "worldtimeapi.org/Europe/Zurich".</summary>
    public string ClockInSource { get; private set; } = string.Empty;
    public string? ClockOutSource { get; private set; }

    /// <summary>Computed shift duration; null while the session is still open.</summary>
    public TimeSpan? Duration => ClockOutUtc.HasValue ? ClockOutUtc.Value - ClockInUtc : null;

    /// <summary>True when the system auto-closed this session (e.g. exceeded the timeout window).</summary>
    public bool IsAutoTimeout { get; private set; }
    public bool IsManualCorrection { get; private set; }

    /// <summary>The employee (manager/admin) who manually corrected this log.</summary>
    public Guid? CorrectedByEmployeeId { get; private set; }
    public string? CorrectionNotes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastModifiedAt { get; private set; }

    /// <summary>A session is open until it has been clocked out.</summary>
    public bool IsOpen => !ClockOutUtc.HasValue;

    // EF Core constructor
    private AttendanceLog() { }

    public static AttendanceLog CreateClockIn(Guid employeeId, DateTimeOffset clockInUtc, string source)
    {
        if (employeeId == Guid.Empty) throw new ArgumentException("EmployeeId is required", nameof(employeeId));
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Clock-in source is required", nameof(source));

        return new AttendanceLog
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            ClockInUtc = clockInUtc,
            ClockInSource = source,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void RecordClockOut(DateTimeOffset clockOutUtc, string source)
    {
        if (!IsOpen)
            throw new InvalidOperationException($"Attendance log {Id} is already closed.");
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Clock-out source is required", nameof(source));
        if (clockOutUtc <= ClockInUtc)
            throw new ArgumentException("Clock-out time must be after clock-in time.", nameof(clockOutUtc));

        ClockOutUtc = clockOutUtc;
        ClockOutSource = source;
        LastModifiedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyAutoTimeout(DateTimeOffset timeoutUtc)
    {
        if (!IsOpen)
            throw new InvalidOperationException($"Attendance log {Id} is already closed.");
        if (timeoutUtc <= ClockInUtc)
            throw new ArgumentException("Timeout time must be after clock-in time.", nameof(timeoutUtc));

        ClockOutUtc = timeoutUtc;
        ClockOutSource = ClockInSource;
        IsAutoTimeout = true;
        LastModifiedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyManualCorrection(DateTimeOffset? newClockIn, DateTimeOffset? newClockOut, Guid correctedBy, string notes)
    {
        if (correctedBy == Guid.Empty)
            throw new ArgumentException("CorrectedBy is required", nameof(correctedBy));
        if (string.IsNullOrWhiteSpace(notes))
            throw new ArgumentException("Correction notes are required", nameof(notes));

        var effectiveClockIn = newClockIn ?? ClockInUtc;
        var effectiveClockOut = newClockOut ?? ClockOutUtc;

        if (effectiveClockOut.HasValue && effectiveClockOut.Value <= effectiveClockIn)
            throw new ArgumentException("Clock-out time must be after clock-in time.", nameof(newClockOut));

        if (newClockIn.HasValue) ClockInUtc = newClockIn.Value;
        if (newClockOut.HasValue) ClockOutUtc = newClockOut.Value;

        IsManualCorrection = true;
        CorrectedByEmployeeId = correctedBy;
        CorrectionNotes = notes;
        LastModifiedAt = DateTimeOffset.UtcNow;
    }
}
