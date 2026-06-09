namespace AttendanceSystem.Infrastructure.ExternalServices.Time;

public class TimeApiSettings
{
    public const string SectionName = "TimeApi";
    public string BaseUrl { get; set; } = "http://worldtimeapi.org/api";
    public string Timezone { get; set; } = "Europe/Zurich";
    public int TimeoutSeconds { get; set; } = 3;
    public int RetryCount { get; set; } = 2;
    public int CircuitBreakerThreshold { get; set; } = 3;
    public int CircuitBreakerDurationSeconds { get; set; } = 30;
    public int CacheTtlSeconds { get; set; } = 5;
}
