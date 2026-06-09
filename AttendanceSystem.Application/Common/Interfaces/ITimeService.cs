using AttendanceSystem.Domain.ValueObjects;

namespace AttendanceSystem.Application.Common.Interfaces;

public interface ITimeService
{
    /// <summary>
    /// Gets the current time for Europe/Zurich from an external API.
    /// NEVER uses server local time or browser time.
    /// Throws TimeServiceUnavailableException if the API cannot be reached.
    /// </summary>
    Task<ZurichTime> GetCurrentTimeAsync(CancellationToken ct = default);

    /// <summary>Returns true if the time service is currently healthy (circuit closed).</summary>
    Task<bool> IsHealthyAsync(CancellationToken ct = default);
}
