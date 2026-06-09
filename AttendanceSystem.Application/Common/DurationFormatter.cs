namespace AttendanceSystem.Application.Common;

public static class DurationFormatter
{
    public const string InProgress = "In progress";

    private static readonly TimeZoneInfo ZurichTz = ResolveZurich();

    private static TimeZoneInfo ResolveZurich()
    {
        // .NET 8 accepts IANA ids on all platforms, but fall back to the Windows id just in case.
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
    }

    /// <summary>Formats a span as "Xh Ym" (e.g. "7h 43m"). Hours can exceed 24.</summary>
    public static string Format(TimeSpan duration)
    {
        var totalHours = (int)duration.TotalHours;
        return $"{totalHours}h {duration.Minutes}m";
    }

    /// <summary>Formats an optional span; null (open session) renders as "In progress".</summary>
    public static string FormatOrInProgress(TimeSpan? duration) =>
        duration.HasValue ? Format(duration.Value) : InProgress;

    /// <summary>
    /// True when clock-in and clock-out fall on different calendar days in Europe/Zurich
    /// (e.g. a 22:45 → 06:30 shift). Open sessions are never "crossing".
    /// </summary>
    public static bool CrossesMidnight(DateTimeOffset clockIn, DateTimeOffset? clockOut)
    {
        if (!clockOut.HasValue) return false;
        var inZurich = TimeZoneInfo.ConvertTime(clockIn, ZurichTz).Date;
        var outZurich = TimeZoneInfo.ConvertTime(clockOut.Value, ZurichTz).Date;
        return inZurich != outZurich;
    }

    /// <summary>
    /// "Xh Ym", with a "(crosses midnight)" suffix when the shift spans two Zurich calendar days.
    /// Returns "In progress" for open sessions. Stored times are UTC, so the math is DST-safe.
    /// </summary>
    public static string FormatDuration(DateTimeOffset clockIn, DateTimeOffset? clockOut)
    {
        if (!clockOut.HasValue) return InProgress;
        var duration = clockOut.Value - clockIn;
        var text = Format(duration);
        return CrossesMidnight(clockIn, clockOut) ? $"{text} (crosses midnight)" : text;
    }
}
