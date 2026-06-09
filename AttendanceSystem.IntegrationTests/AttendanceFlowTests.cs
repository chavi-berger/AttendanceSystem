using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AttendanceSystem.Domain.Constants;
using AttendanceSystem.Domain.Entities;
using AttendanceSystem.Infrastructure.BackgroundJobs;
using AttendanceSystem.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using BCryptNet = BCrypt.Net.BCrypt;

namespace AttendanceSystem.IntegrationTests;

public class AttendanceFlowTests : IClassFixture<IntegrationApiFactory>
{
    private readonly IntegrationApiFactory _factory;

    public AttendanceFlowTests(IntegrationApiFactory factory) => _factory = factory;

    // ---------------------------------------------------------------- Test 1
    [SqlServerFact]
    public async Task Test1_FullHappyPath_UsesMockTimeNotServerTime()
    {
        _factory.MockTime.SetAvailable();
        _factory.MockTime.SetTime(new DateTimeOffset(2025, 3, 10, 8, 0, 0, TimeSpan.FromHours(1)));

        var (token, _, _) = await RegisterAndLoginAsync(Roles.Employee);
        var client = AuthedClient(token);

        var clockIn = await PostJson(client, "/api/attendance/clock-in", "{}");
        clockIn.StatusCode.Should().Be(HttpStatusCode.OK);
        var clockInUtc = DateTimeOffset.Parse(await ReadProp(clockIn, "clockInUtc"));
        // Time came from the mock time service (year 2025), NOT the server clock (year 2026+).
        clockInUtc.Year.Should().Be(2025);
        clockInUtc.Year.Should().NotBe(DateTimeOffset.UtcNow.Year);

        var status1 = await client.GetAsync("/api/attendance/status");
        (await ReadProp(status1, "isClockedIn")).Should().Be("True");

        var clockOut = await PostJson(client, "/api/attendance/clock-out", "{}");
        clockOut.StatusCode.Should().Be(HttpStatusCode.OK);

        var status2 = await client.GetAsync("/api/attendance/status");
        (await ReadProp(status2, "isClockedIn")).Should().Be("False");
    }

    // ---------------------------------------------------------------- Test 2
    [SqlServerFact]
    public async Task Test2_DoubleClockIn_Returns409()
    {
        _factory.MockTime.SetAvailable();
        var (token, _, _) = await RegisterAndLoginAsync(Roles.Employee);
        var client = AuthedClient(token);

        (await PostJson(client, "/api/attendance/clock-in", "{}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostJson(client, "/api/attendance/clock-in", "{}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---------------------------------------------------------------- Test 3
    [SqlServerFact]
    public async Task Test3_ClockOutWithoutClockIn_Returns400()
    {
        _factory.MockTime.SetAvailable();
        var (token, _, _) = await RegisterAndLoginAsync(Roles.Employee);
        var client = AuthedClient(token);

        var res = await PostJson(client, "/api/attendance/clock-out", "{}");
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- Test 4
    [SqlServerFact]
    public async Task Test4_TimeServiceDown_Returns503_AndNoRecordCreated()
    {
        var (token, _, employeeId) = await RegisterAndLoginAsync(Roles.Employee);
        var client = AuthedClient(token);

        _factory.MockTime.SetUnavailable();
        try
        {
            var res = await PostJson(client, "/api/attendance/clock-in", "{}");
            res.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            res.Headers.TryGetValues("Retry-After", out _).Should().BeTrue();

            // CRITICAL: nothing was persisted when the time source was unavailable.
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.AttendanceLogs.AnyAsync(a => a.EmployeeId == employeeId)).Should().BeFalse();
        }
        finally
        {
            _factory.MockTime.SetAvailable();
        }
    }

    // ---------------------------------------------------------------- Test 5
    [SqlServerFact]
    public async Task Test5_MidnightCrossing_RecordsCorrectDuration()
    {
        _factory.MockTime.SetAvailable();
        var (token, _, _) = await RegisterAndLoginAsync(Roles.Employee);
        var client = AuthedClient(token);

        _factory.MockTime.SetTime(new DateTimeOffset(2025, 1, 14, 23, 55, 0, TimeSpan.FromHours(1)));
        (await PostJson(client, "/api/attendance/clock-in", "{}")).StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.MockTime.SetTime(new DateTimeOffset(2025, 1, 15, 0, 5, 0, TimeSpan.FromHours(1)));
        var clockOut = await PostJson(client, "/api/attendance/clock-out", "{}");
        clockOut.StatusCode.Should().Be(HttpStatusCode.OK);

        // 23:55:01 -> 00:05:01 == 10 minutes.
        (await ReadProp(clockOut, "durationFormatted")).Should().Be("0h 10m");

        var history = await client.GetAsync("/api/attendance/history");
        var json = await history.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var first = doc.RootElement.GetProperty("items")[0];
        first.GetProperty("crossesMidnight").GetBoolean().Should().BeTrue();
        first.GetProperty("durationFormatted").GetString().Should().Contain("crosses midnight");
    }

    // ---------------------------------------------------------------- Test 6
    [SqlServerFact]
    public async Task Test6_AutoTimeoutJob_ClosesStaleSession()
    {
        var (_, _, employeeId) = await RegisterAndLoginAsync(Roles.Employee);

        // Insert an open session that started 20 hours ago (> AutoTimeoutHours = 16).
        Guid logId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var log = AttendanceLog.CreateClockIn(employeeId, DateTimeOffset.UtcNow.AddHours(-20), "test://seed");
            db.AttendanceLogs.Add(log);
            await db.SaveChangesAsync();
            logId = log.Id;
        }

        // Trigger the job directly.
        var job = _factory.Services.GetRequiredService<OpenSessionTimeoutJob>();
        var closed = await job.RunOnceAsync(CancellationToken.None);
        closed.Should().BeGreaterThanOrEqualTo(1);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var log = await db.AttendanceLogs.AsNoTracking().FirstAsync(a => a.Id == logId);
            log.ClockOutUtc.Should().NotBeNull();
            log.IsAutoTimeout.Should().BeTrue();

            var audit = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityId == logId && a.Action == "AutoTimeout")
                .ToListAsync();
            audit.Should().NotBeEmpty();
        }
    }

    // ---------------------------------------------------------------- Test 7
    [SqlServerFact]
    public async Task Test7_ManualCorrection_WritesAuditTrail()
    {
        _factory.MockTime.SetAvailable();
        // Build a completed session for an employee.
        var (empToken, _, _) = await RegisterAndLoginAsync(Roles.Employee);
        var empClient = AuthedClient(empToken);
        await PostJson(empClient, "/api/attendance/clock-in", "{}");
        var clockOut = await PostJson(empClient, "/api/attendance/clock-out", "{}");
        var logId = await ReadProp(clockOut, "logId");

        // Admin corrects it.
        var adminToken = await LoginSeededAsync("admin@company.ch");
        var adminClient = AuthedClient(adminToken);
        var body = JsonSerializer.Serialize(new
        {
            newClockIn = "2025-03-01T08:00:00+01:00",
            newClockOut = "2025-03-01T16:30:00+01:00",
            reason = "Employee forgot to clock in on time",
        });
        var res = await PostJson(adminClient, $"/api/attendance/{logId}/correct", body, HttpMethod.Put);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.AttendanceLogs.AsNoTracking().FirstAsync(a => a.Id == Guid.Parse(logId));
        log.IsManualCorrection.Should().BeTrue();

        var audit = await db.AuditLogs.AsNoTracking()
            .FirstAsync(a => a.EntityId == Guid.Parse(logId) && a.Action == "ManualCorrection");
        audit.OldValueJson.Should().NotBeNullOrEmpty();
        audit.NewValueJson.Should().NotBeNullOrEmpty();
    }

    // ---------------------------------------------------------------- Test 8
    [SqlServerFact]
    public async Task Test8_RateLimiting_SixthRequestReturns429()
    {
        _factory.MockTime.SetAvailable();
        var (token, _, _) = await RegisterAndLoginAsync(Roles.Employee);
        var client = AuthedClient(token);

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
            codes.Add((await PostJson(client, "/api/attendance/clock-in", "{}")).StatusCode);

        // First 5 are processed (200 then 409s); the 6th exceeds the 5/min limit.
        codes.Take(5).Should().OnlyContain(c => c == HttpStatusCode.OK || c == HttpStatusCode.Conflict);
        codes[5].Should().Be(HttpStatusCode.TooManyRequests);
    }

    // ---------------------------------------------------------------- Test 9
    [SqlServerFact]
    public async Task Test9_ExpiredToken_Returns401_ThenRefreshWorks()
    {
        _factory.MockTime.SetAvailable();
        var (_, refreshToken, employeeId) = await RegisterAndLoginAsync(Roles.Employee);

        // An expired access token must be rejected.
        var expiredClient = AuthedClient(CreateExpiredToken(employeeId));
        (await PostJson(expiredClient, "/api/attendance/clock-in", "{}")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        // Refresh -> new access token -> works.
        var anon = _factory.CreateClient();
        var refreshRes = await PostJson(anon, "/api/auth/refresh",
            JsonSerializer.Serialize(new { refreshToken }));
        refreshRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var newToken = await ReadProp(refreshRes, "accessToken");

        var refreshed = AuthedClient(newToken);
        (await PostJson(refreshed, "/api/attendance/clock-in", "{}")).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------- Test 10
    [SqlServerFact]
    public async Task Test10_ActiveEndpoint_AdminAllowed_EmployeeForbidden()
    {
        _factory.MockTime.SetAvailable();
        var (empToken, _, _) = await RegisterAndLoginAsync(Roles.Employee);
        var empClient = AuthedClient(empToken);
        await PostJson(empClient, "/api/attendance/clock-in", "{}");

        (await empClient.GetAsync("/api/attendance/active")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var adminClient = AuthedClient(await LoginSeededAsync("admin@company.ch"));
        var adminRes = await adminClient.GetAsync("/api/attendance/active");
        adminRes.StatusCode.Should().Be(HttpStatusCode.OK);
        (await adminRes.Content.ReadAsStringAsync()).Should().StartWith("[");
    }

    // ================================================================ helpers

    private HttpClient AuthedClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(string accessToken, string refreshToken, Guid employeeId)> RegisterAndLoginAsync(string role)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var email = $"user_{suffix}@test.ch";
        var badge = $"T{suffix}".ToUpperInvariant();

        Guid id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var emp = Employee.Create($"Test {suffix}", email, badge, BCryptNet.HashPassword("Test@1234"), role);
            db.Employees.Add(emp);
            await db.SaveChangesAsync();
            id = emp.Id;
        }

        var (access, refresh) = await LoginAsync(email, "Test@1234");
        return (access, refresh, id);
    }

    private async Task<string> LoginSeededAsync(string email)
    {
        var (access, _) = await LoginAsync(email, "Test@1234");
        return access;
    }

    private async Task<(string accessToken, string refreshToken)> LoginAsync(string email, string password)
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsync("/api/auth/login",
            new StringContent(JsonSerializer.Serialize(new { email, password }), Encoding.UTF8, "application/json"));
        res.StatusCode.Should().Be(HttpStatusCode.OK, "login should succeed for seeded/created users");
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return (
            doc.RootElement.GetProperty("accessToken").GetString()!,
            doc.RootElement.GetProperty("refreshToken").GetString()!);
    }

    private static Task<HttpResponseMessage> PostJson(HttpClient client, string url, string json, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return client.SendAsync(request);
    }

    private static async Task<string> ReadProp(HttpResponseMessage res, string prop)
    {
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var el = doc.RootElement.GetProperty(prop);
        return el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False
            ? el.GetBoolean().ToString()
            : el.ToString();
    }

    private static string CreateExpiredToken(Guid employeeId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(IntegrationApiFactory.JwtSecret));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, employeeId.ToString()),
                new Claim(ClaimTypes.NameIdentifier, employeeId.ToString()),
                new Claim(ClaimTypes.Role, "Employee"),
            }),
            Expires = DateTime.UtcNow.AddMinutes(-5),
            NotBefore = DateTime.UtcNow.AddMinutes(-10),
            IssuedAt = DateTime.UtcNow.AddMinutes(-10),
            Issuer = IntegrationApiFactory.JwtIssuer,
            Audience = IntegrationApiFactory.JwtAudience,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
