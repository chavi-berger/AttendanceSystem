using System;
using AttendanceSystem.Domain.Entities;
using AttendanceSystem.Domain.Exceptions;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AttendanceSystem.UnitTests.Domain;

public class AttendanceDomainTests
{
    private const string Source = "worldtimeapi.org/Europe/Zurich";

    [Fact]
    public void AttendanceLog_IsOpen_TrueAfterClockIn_FalseAfterClockOut()
    {
        var clockIn = new DateTimeOffset(2025, 1, 15, 9, 0, 0, TimeSpan.FromHours(1));
        var log = AttendanceLog.CreateClockIn(Guid.NewGuid(), clockIn, Source);

        log.IsOpen.Should().BeTrue();
        log.Duration.Should().BeNull();

        log.RecordClockOut(clockIn.AddHours(8), Source);

        log.IsOpen.Should().BeFalse();
        log.ClockOutUtc.Should().NotBeNull();
        log.Duration.Should().Be(TimeSpan.FromHours(8));
    }

    [Fact]
    public void RecordClockOut_Throws_WhenClockOutNotAfterClockIn()
    {
        var clockIn = DateTimeOffset.UtcNow;
        var log = AttendanceLog.CreateClockIn(Guid.NewGuid(), clockIn, Source);

        var act = () => log.RecordClockOut(clockIn, Source); // equal => invalid

        act.Should().Throw<ArgumentException>()
           .WithParameterName("clockOutUtc");
        log.IsOpen.Should().BeTrue(); // state unchanged after failed clock-out
    }

    [Fact]
    public void AlreadyClockedInException_StoresExistingSessionTime()
    {
        var employeeId = Guid.NewGuid();
        var existing = new DateTimeOffset(2025, 1, 15, 9, 0, 0, TimeSpan.FromHours(1));

        var ex = new AlreadyClockedInException(employeeId, existing);

        ex.EmployeeId.Should().Be(employeeId);
        ex.ExistingClockInTime.Should().Be(existing);
        ex.Message.Should().Contain(employeeId.ToString());
        ex.Message.Should().Contain(existing.ToString("O"));
    }

    [Fact]
    public void Employee_Create_NormalizesEmailAndBadge()
    {
        var employee = Employee.Create("  Jane Doe  ", "Jane.Doe@Example.COM", " badge-7 ", "hash");

        employee.FullName.Should().Be("Jane Doe");
        employee.Email.Should().Be("jane.doe@example.com");
        employee.BadgeNumber.Should().Be("BADGE-7");
        employee.IsActive.Should().BeTrue();
        employee.Role.Should().Be("Employee");
        employee.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void ZurichTime_EqualityIsByValueOnly()
    {
        var instant = new DateTimeOffset(2025, 1, 15, 9, 0, 0, TimeSpan.FromHours(1));

        var a = ZurichTime.FromApiResponse(instant, "source-a");
        var b = ZurichTime.FromApiResponse(instant, "source-b"); // different source/receipt time

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());

        var different = ZurichTime.FromApiResponse(instant.AddSeconds(1), "source-a");
        (a != different).Should().BeTrue();
    }
}
