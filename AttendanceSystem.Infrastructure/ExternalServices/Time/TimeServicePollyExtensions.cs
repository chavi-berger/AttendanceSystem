using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;

namespace AttendanceSystem.Infrastructure.ExternalServices.Time;

public static class TimeServicePollyExtensions
{
    /// <summary>
    /// Registers the WorldTimeApiClient typed HttpClient with a Polly resilience pipeline:
    /// retry (outer) -> circuit breaker -> per-request timeout (inner).
    /// </summary>
    public static IServiceCollection AddTimeServiceWithPolly(this IServiceCollection services, TimeApiSettings settings)
    {
        // The circuit breaker MUST be a single shared instance so its state spans requests.
        // It is built lazily on first request so we can resolve the real DI logger.
        IAsyncPolicy<HttpResponseMessage>? retry = null;
        IAsyncPolicy<HttpResponseMessage>? breaker = null;
        IAsyncPolicy<HttpResponseMessage>? timeout = null;

        services.AddHttpClient<WorldTimeApiClient>(client =>
        {
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds + 2); // outer (HttpClient) timeout
        })
        .AddPolicyHandler((sp, _) => retry ??= GetRetryPolicy(sp.GetRequiredService<ILoggerFactory>(), settings))
        .AddPolicyHandler((sp, _) => breaker ??= GetCircuitBreakerPolicy(sp.GetRequiredService<ILoggerFactory>(), settings))
        .AddPolicyHandler((sp, _) => timeout ??= GetTimeoutPolicy(settings));

        return services;
    }

    // Retry: exponential backoff (0.5s, 1s...), only on transient errors + Polly timeouts.
    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(ILoggerFactory loggerFactory, TimeApiSettings settings)
    {
        var logger = loggerFactory.CreateLogger("TimeService.Retry");
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .WaitAndRetryAsync(
                settings.RetryCount,
                attempt => TimeSpan.FromSeconds(0.5 * Math.Pow(2, attempt - 1)),
                onRetry: (outcome, delay, attempt, _) =>
                    logger.LogWarning("Time service retry {Attempt} after {Delay}s due to {Reason}",
                        attempt, delay.TotalSeconds, outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()));
    }

    // Circuit breaker: opens after N consecutive failures, stays open for the configured duration.
    private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(ILoggerFactory loggerFactory, TimeApiSettings settings)
    {
        var logger = loggerFactory.CreateLogger("TimeService.CircuitBreaker");
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: settings.CircuitBreakerThreshold,
                durationOfBreak: TimeSpan.FromSeconds(settings.CircuitBreakerDurationSeconds),
                onBreak: (_, _) => logger.LogError("Circuit breaker OPEN — time service unavailable"),
                onReset: () => logger.LogInformation("Circuit breaker CLOSED — time service recovered"),
                onHalfOpen: () => logger.LogWarning("Circuit breaker HALF-OPEN — testing time service"));
    }

    // Per-request pessimistic timeout.
    private static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy(TimeApiSettings settings) =>
        Policy.TimeoutAsync<HttpResponseMessage>(
            TimeSpan.FromSeconds(settings.TimeoutSeconds), TimeoutStrategy.Pessimistic);
}
