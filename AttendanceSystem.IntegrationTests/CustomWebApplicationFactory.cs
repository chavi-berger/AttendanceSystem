using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.MsSql;
using Xunit;

namespace AttendanceSystem.IntegrationTests;

/// <summary>
/// Boots the API against a throwaway SQL Server container (Testcontainers).
/// Program.cs applies migrations and seeds on startup, so the schema is created automatically.
/// Container startup is tolerant: if Docker is unavailable the error is captured and tests
/// guarded by <see cref="DockerAvailableFactAttribute"/> are skipped rather than failing.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _dbContainer = new MsSqlBuilder()
        .WithPassword("Attendance@Strong123!")
        .Build();

    public string? StartupError { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            await _dbContainer.StartAsync();
        }
        catch (Exception ex)
        {
            StartupError = ex.Message; // Docker missing/unreachable — guarded tests will skip
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _dbContainer.GetConnectionString(),
                ["UseTimeMock"] = "true" // avoid hitting the real time API during tests
            });
        });
    }

    public new async Task DisposeAsync()
    {
        await _dbContainer.DisposeAsync();
        await base.DisposeAsync();
    }
}
