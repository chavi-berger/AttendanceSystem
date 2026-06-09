using System.Net;
using System.Text;
using AttendanceSystem.Domain.Exceptions;
using AttendanceSystem.Domain.ValueObjects;
using AttendanceSystem.Infrastructure.ExternalServices.Time;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AttendanceSystem.UnitTests;

public class TimeServiceTests
{
    private const string ValidJson =
        "{\"datetime\":\"2025-01-15T09:00:00+01:00\",\"utc_offset\":\"+01:00\"," +
        "\"timezone\":\"Europe/Zurich\",\"utc_datetime\":\"2025-01-15T08:00:00+00:00\"," +
        "\"dst\":false,\"raw_offset\":3600,\"dst_offset\":0}";

    private static TimeApiSettings Settings(int ttl = 5, int retry = 2, int threshold = 3) => new()
    {
        BaseUrl = "http://worldtimeapi.org/api",
        Timezone = "Europe/Zurich",
        TimeoutSeconds = 3,
        RetryCount = retry,
        CircuitBreakerThreshold = threshold,
        CircuitBreakerDurationSeconds = 30,
        CacheTtlSeconds = ttl
    };

    private static WorldTimeApiClient Client(HttpMessageHandler handler, TimeApiSettings s) =>
        new(new HttpClient(handler), NullLogger<WorldTimeApiClient>.Instance, Options.Create(s));

    private static CachedTimeService Cached(WorldTimeApiClient client, TimeApiSettings s, IMemoryCache cache) =>
        new(client, cache, NullLogger<CachedTimeService>.Instance, Options.Create(s));

    private static IMemoryCache NewCache() => new MemoryCache(new MemoryCacheOptions());

    [Fact]
    public async Task GetCurrentTimeAsync_ReturnsZurichTime_WhenApiAvailable()
    {
        var s = Settings();
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK, ValidJson));
        var sut = Cached(Client(handler, s), s, NewCache());

        var result = await sut.GetCurrentTimeAsync();

        result.Should().BeOfType<ZurichTime>();
        result.Value.Should().Be(new DateTimeOffset(2025, 1, 15, 9, 0, 0, TimeSpan.FromHours(1)));
        result.Source.Should().Contain("Europe/Zurich");
    }

    [Fact]
    public async Task GetCurrentTimeAsync_ThrowsTimeServiceUnavailableException_WhenHttpFails()
    {
        var s = Settings();
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.InternalServerError, "boom"));
        var sut = Cached(Client(handler, s), s, NewCache());

        var act = async () => await sut.GetCurrentTimeAsync();

        await act.Should().ThrowAsync<TimeServiceUnavailableException>();
    }

    [Fact]
    public async Task GetCurrentTimeAsync_ThrowsTimeServiceUnavailableException_WhenTimeout()
    {
        var s = Settings();
        // Simulate a timeout: handler throws TaskCanceledException while caller's token is NOT cancelled.
        var handler = new StubHandler((_, _) => throw new TaskCanceledException("simulated timeout"));
        var sut = Cached(Client(handler, s), s, NewCache());

        var act = async () => await sut.GetCurrentTimeAsync();

        await act.Should().ThrowAsync<TimeServiceUnavailableException>();
    }

    [Fact]
    public async Task GetCurrentTimeAsync_ReturnsCachedValue_WithinTtl()
    {
        var s = Settings(ttl: 30);
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK, ValidJson));
        var sut = Cached(Client(handler, s), s, NewCache());

        await sut.GetCurrentTimeAsync();
        await sut.GetCurrentTimeAsync();

        handler.CallCount.Should().Be(1, "the second call within TTL must come from cache");
    }

    [Fact]
    public async Task GetCurrentTimeAsync_CallsApiAgain_AfterCacheExpiry()
    {
        var s = Settings(ttl: 1);
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK, ValidJson));
        var sut = Cached(Client(handler, s), s, NewCache());

        await sut.GetCurrentTimeAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(1200)); // let the 1s TTL expire
        await sut.GetCurrentTimeAsync();

        handler.CallCount.Should().Be(2, "after the TTL expires the API must be hit again");
    }

    [Fact]
    public async Task MockTimeService_ThrowsException_WhenSetToUnavailable()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var mock = new MockTimeService(config);
        mock.SetUnavailable();

        var act = async () => await mock.GetCurrentTimeAsync();

        await act.Should().ThrowAsync<TimeServiceUnavailableException>();
    }

    [Fact]
    public async Task CircuitBreaker_OpensAfterThresholdFailures()
    {
        // RetryCount=0 so each call is exactly one attempt; breaker opens after 3 failures.
        var s = Settings(retry: 0, threshold: 3);
        var handler = new CountingFailHandler();

        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<TimeApiSettings>(o =>
        {
            o.BaseUrl = s.BaseUrl; o.Timezone = s.Timezone; o.TimeoutSeconds = s.TimeoutSeconds;
            o.RetryCount = 0; o.CircuitBreakerThreshold = 3; o.CircuitBreakerDurationSeconds = 30;
        });
        services.AddTimeServiceWithPolly(s);
        services.AddHttpClient<WorldTimeApiClient>().ConfigurePrimaryHttpMessageHandler(() => handler);

        var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<WorldTimeApiClient>();

        // 3 real failures open the circuit...
        for (var i = 0; i < 3; i++)
        {
            var attempt = async () => await client.GetZurichTimeAsync();
            await attempt.Should().ThrowAsync<TimeServiceUnavailableException>();
        }

        // ...the 4th call must fail fast WITHOUT hitting the API.
        var fastFail = async () => await client.GetZurichTimeAsync();
        await fastFail.Should().ThrowAsync<TimeServiceUnavailableException>();

        handler.CallCount.Should().Be(3, "once open, the breaker short-circuits without calling the API");
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;
        public int CallCount { get; private set; }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder) =>
            _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_responder(request, ct));
        }
    }

    private sealed class CountingFailHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
