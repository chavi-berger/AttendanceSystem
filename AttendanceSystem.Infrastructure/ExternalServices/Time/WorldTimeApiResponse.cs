using System.Globalization;
using System.Text.Json.Serialization;

namespace AttendanceSystem.Infrastructure.ExternalServices.Time;

public class WorldTimeApiResponse
{
    // --- worldtimeapi.org shape (datetime carries the UTC offset) ---
    [JsonPropertyName("datetime")]
    public string Datetime { get; set; } = string.Empty;

    [JsonPropertyName("utc_offset")]
    public string UtcOffset { get; set; } = string.Empty;

    [JsonPropertyName("timezone")]
    public string Timezone { get; set; } = string.Empty;

    [JsonPropertyName("utc_datetime")]
    public string UtcDatetime { get; set; } = string.Empty;

    [JsonPropertyName("dst")]
    public bool Dst { get; set; }  // true during summer time

    [JsonPropertyName("raw_offset")]
    public int RawOffset { get; set; }

    [JsonPropertyName("dst_offset")]
    public int DstOffset { get; set; }

    // --- timeapi.io shape (local wall-clock time + named zone, no offset) ---
    [JsonPropertyName("dateTime")]
    public string DateTimeLocal { get; set; } = string.Empty;

    [JsonPropertyName("timeZone")]
    public string TimeZoneName { get; set; } = string.Empty;

    /// <summary>
    /// Parses the response into a DateTimeOffset. Supports both providers:
    /// worldtimeapi's offset-qualified "datetime", and timeapi.io's local "dateTime" + named zone
    /// (the offset is derived from the zone, which is DST-aware).
    /// </summary>
    public DateTimeOffset ParsedDateTime
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Datetime))
                return DateTimeOffset.Parse(Datetime, null, DateTimeStyles.RoundtripKind);

            if (!string.IsNullOrWhiteSpace(DateTimeLocal))
            {
                var local = DateTime.Parse(DateTimeLocal, CultureInfo.InvariantCulture, DateTimeStyles.None);
                local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
                var tz = ResolveTimeZone(TimeZoneName);
                return new DateTimeOffset(local, tz.GetUtcOffset(local));
            }

            throw new FormatException("Time API response did not contain a recognizable datetime field.");
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        var zone = string.IsNullOrWhiteSpace(id) ? "Europe/Zurich" : id;
        try { return TimeZoneInfo.FindSystemTimeZoneById(zone); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
    }
}
