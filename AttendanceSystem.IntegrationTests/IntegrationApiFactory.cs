using System.Linq;
using AttendanceSystem.Application.Common.Interfaces;
using AttendanceSystem.Infrastructure.ExternalServices.Time;
using AttendanceSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AttendanceSystem.IntegrationTests;

/// <summary>
/// Boots the full API in-process (TestServer) against a freshly-created, isolated SQL Server
/// database (LocalDB by default, or ATTENDANCE_TEST_CONNECTION in CI). The database is migrated
/// + seeded on startup and dropped on dispose.
///
/// The DbContext and time service are swapped via ConfigureTestServices (which runs AFTER
/// Program's registrations) so we deterministically use the test DB + the mock time source —
/// the minimal-hosting config pipeline cannot reliably override AddInfrastructure's reads.
/// </summary>
public class IntegrationApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Must match appsettings.json so the app validates tokens (incl. the crafted expired token in tests).
    public const string JwtSecret = "REPLACE_WITH_32_CHAR_SECRET_IN_USER_SECRETS";
    public const string JwtIssuer = "AttendanceSystem";
    public const string JwtAudience = "AttendanceSystemClients";

    private readonly string _dbName = $"AttendanceTests_{Guid.NewGuid():N}";
    private readonly string _connectionString;

    public IntegrationApiFactory()
    {
        _connectionString = TestDatabase.UniqueDatabaseConnectionString(_dbName);
    }

    /// <summary>The deterministic mock time source, for tests that control time.</summary>
    public MockTimeService MockTime => (MockTimeService)Services.GetRequiredService<ITimeService>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing"); // disables HTTPS redirect in the test server

        builder.ConfigureTestServices(services =>
        {
            // --- Replace the DbContext with one pointed at the isolated test database ---
            foreach (var d in services.Where(d =>
                         d.ServiceType == typeof(AppDbContext) ||
                         (d.ServiceType.FullName?.Contains("DbContextOptions") ?? false)).ToList())
            {
                services.Remove(d);
            }

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(_connectionString, sql =>
                    sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

            // --- Force the deterministic mock time source ---
            services.RemoveAll(typeof(ITimeService));
            services.AddSingleton<ITimeService, MockTimeService>();
        });
    }

    public Task InitializeAsync()
    {
        // Force the host to build, which runs Program's migrate + seed against the test database.
        using var _ = CreateClient();
        return Task.CompletedTask;
    }

    public new async Task DisposeAsync()
    {
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureDeletedAsync();
        }
        await base.DisposeAsync();
    }
}
