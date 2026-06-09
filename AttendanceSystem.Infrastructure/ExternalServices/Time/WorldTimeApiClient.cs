// ARCHITECTURE DECISION: This service is the ONLY source of time for attendance operations.
// DateTime.Now, DateTime.UtcNow, and browser time are NEVER used for Clock-In/Out.
using System.Text.Json;
using AttendanceSystem.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace AttendanceSystem.Infrastructure.ExternalServices.Time;

public class WorldTimeApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WorldTimeApiClient> _logger;
    private readonly TimeApiSettings _settings;

    public WorldTimeApiClient(HttpClient httpClient, ILogger<WorldTimeApiClient> logger, IOptions<TimeApiSettings> settings)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settings = settings.Value;
    }

    // URL: http://worldtimeapi.org/api/timezone/Europe/Zurich
    public async Task<WorldTimeApiResponse> GetZurichTimeAsync(CancellationToken ct = default)
    {
        // timeapi.io: GET {BaseUrl}/Time/current/zone?timeZone=Europe/Zurich
        var url = $"{_settings.BaseUrl}/Time/current/zone?timeZone={_settings.Timezone}";
        _logger.LogDebug("Fetching time from {Url}", url);

        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<WorldTimeApiResponse>(json)
                ?? throw new InvalidOperationException("Received null response from time API");

            _logger.LogInformation("Successfully fetched Zurich time: {Time} (DST: {Dst})",
                result.ParsedDateTime, result.Dst);

            return result;
        }
        catch (BrokenCircuitException ex)
        {
            // Circuit breaker is open — fail fast, do NOT fall back to server time.
            _logger.LogError(ex, "Circuit breaker open while fetching time from {Url}", url);
            throw new TimeServiceUnavailableException("Time service circuit breaker is open", ex);
        }
        catch (TimeoutRejectedException ex)
        {
            // Polly pessimistic timeout fired.
            _logger.LogError(ex, "Polly timeout fetching time from {Url}", url);
            throw new TimeServiceUnavailableException($"Request timed out after {_settings.TimeoutSeconds} seconds", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error fetching time from {Url}", url);
            throw new TimeServiceUnavailableException($"HTTP error: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Timeout fetching time from {Url}", url);
            throw new TimeServiceUnavailableException($"Request timed out after {_settings.TimeoutSeconds} seconds", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse time API response");
            throw new TimeServiceUnavailableException("Invalid response format from time API", ex);
        }
    }
}
