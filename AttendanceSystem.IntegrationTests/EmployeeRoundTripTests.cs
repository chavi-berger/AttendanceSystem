using AttendanceSystem.Application.Common.Interfaces;
using AttendanceSystem.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AttendanceSystem.IntegrationTests;

public class EmployeeRoundTripTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public EmployeeRoundTripTests(CustomWebApplicationFactory factory) => _factory = factory;

    [DockerAvailableFact]
    public async Task CreateEmployee_ThenGetByEmail_RoundTripsThroughSqlServer()
    {
        // Touch the host so Program.cs runs migrations + seeding against the container.
        _ = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var employee = Employee.Create(
            "Round Trip", "roundtrip@company.ch", "RT001", "hashed-password", "Employee");

        await uow.Employees.AddAsync(employee);
        await uow.SaveChangesAsync();

        var fetched = await uow.Employees.GetByEmailAsync("roundtrip@company.ch");

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(employee.Id);
        fetched.FullName.Should().Be("Round Trip");
        fetched.BadgeNumber.Should().Be("RT001");
        fetched.Email.Should().Be("roundtrip@company.ch");
        fetched.IsActive.Should().BeTrue();
    }

    [DockerAvailableFact]
    public async Task Seeder_CreatesThreeEmployees_OnStartup()
    {
        _ = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var active = await uow.Employees.GetAllActiveAsync();

        active.Should().Contain(e => e.BadgeNumber == "EMP001");
        active.Should().Contain(e => e.BadgeNumber == "EMP002");
        active.Should().Contain(e => e.BadgeNumber == "ADM001");
    }
}
