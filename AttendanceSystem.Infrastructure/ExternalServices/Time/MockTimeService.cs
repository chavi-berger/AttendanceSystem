using AttendanceSystem.Application.Common.Interfaces;
using AttendanceSystem.Domain.Exceptions;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;

namespace AttendanceSystem.Infrastructure.ExternalServices.Time;

/// <summary>
/// Deterministic time source for Development/Testing. Still routes through ITimeService so the
/// rest of the system never reads the server clock directly.
/// </summary>
public class MockTimeService : ITimeService
{
    private DateTimeOffset _currentTime;
    private bool _isAvailable = true;

    public MockTimeService(IConfiguration config)
    {
        var mockTime = config["MockTime"];
        _currentTime = mockTime != null
            ? DateTimeOffset.Parse(mockTime)
            : DateTimeOffset.UtcNow;
    }

    public Task<ZurichTime> GetCurrentTimeAsync(CancellationToken ct = default)
    {
        if (!_isAvailable)
            throw new TimeServiceUnavailableException("Mock time service is set to unavailable");

        // Advance mock time by 1 second on each call (simulates real time passing)
        _currentTime = _currentTime.AddSeconds(1);
        return Task.FromResult(ZurichTime.FromApiResponse(_currentTime, "mock://development"));
    }

    public Task<bool> IsHealthyAsync(CancellationToken ct = default) =>
        Task.FromResult(_isAvailable);

    // Test helpers
    public void SetTime(DateTimeOffset time) => _currentTime = time;
    public void SetUnavailable() => _isAvailable = false;
    public void SetAvailable()   => _isAvailable = true;
}
