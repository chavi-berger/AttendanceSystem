using AttendanceSystem.Application.Common.Interfaces;
using AttendanceSystem.Domain.Exceptions;
using AttendanceSystem.Domain.ValueObjects;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AttendanceSystem.Infrastructure.ExternalServices.Time;

/// <summary>
/// Decorator over <see cref="WorldTimeApiClient"/> adding a short-lived in-memory cache.
/// The cache is ONLY a performance optimization — every cached value originally came from the
/// external API. There is NO fallback to server time; failures propagate as
/// <see cref="TimeServiceUnavailableException"/>.
/// </summary>
public class CachedTimeService : ITimeService
{
    private const string CacheKey = "zurich_time";

    private readonly WorldTimeApiClient _client;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CachedTimeService> _logger;
    private readonly TimeApiSettings _settings;

    public CachedTimeService(
        WorldTimeApiClient client,
        IMemoryCache cache,
        ILogger<CachedTimeService> logger,
        IOptions<TimeApiSettings> settings)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<ZurichTime> GetCurrentTimeAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out ZurichTime? cached) && cached is not null)
        {
            _logger.LogDebug("Returning CACHED Zurich time {Time} (received {Received})",
                cached.Value, cached.ReceivedAtUtc);
            return cached;
        }

        // Cache miss — call the external API. On failure we do NOT cache and we propagate.
        var response = await _client.GetZurichTimeAsync(ct);
        var source = $"{_settings.BaseUrl}/timezone/{_settings.Timezone}";
        var zurichTime = ZurichTime.FromApiResponse(response.ParsedDateTime, source);

        _cache.Set(CacheKey, zurichTime, TimeSpan.FromSeconds(_settings.CacheTtlSeconds));
        _logger.LogDebug("Cached fresh Zurich time {Time} for {Ttl}s", zurichTime.Value, _settings.CacheTtlSeconds);

        return zurichTime;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            await GetCurrentTimeAsync(ct);
            return true;
        }
        catch (TimeServiceUnavailableException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error during time service health probe");
            return false;
        }
    }
}
