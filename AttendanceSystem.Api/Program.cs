using AttendanceSystem.Api.Extensions;
using AttendanceSystem.Api.Middleware;
using AttendanceSystem.Application;
using AttendanceSystem.Infrastructure;
using AttendanceSystem.Infrastructure.Persistence;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog from configuration.
builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration));

// Services
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddRateLimiting();
builder.Services.AddHealthChecksConfiguration(builder.Configuration);
builder.Services.AddSwaggerWithJwt();
builder.Services.AddCorsForFrontend();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Apply pending migrations and seed initial data on startup.
// Wrapped so a transient DB outage logs loudly instead of crashing the host.
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await DbSeeder.SeedAsync(db);
        logger.LogInformation("Database migrated and seeded successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database migration/seed failed on startup. The API will start, but DB-backed endpoints will fail until the database is reachable.");
    }
}

// Middleware pipeline (ORDER MATTERS)
app.UseSerilogRequestLogging();
app.UseMiddleware<GlobalExceptionMiddleware>();
// Skip HTTPS redirection under the in-process test server (it would drop the Authorization
// header on the http->https cross-origin redirect).
if (!app.Environment.IsEnvironment("Testing"))
    app.UseHttpsRedirection();
app.UseCors(ApiServiceExtensions.CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Attendance API v1"));
}

app.MapControllers();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

await app.RunAsync();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program { }
