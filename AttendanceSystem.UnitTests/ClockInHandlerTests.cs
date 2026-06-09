using AttendanceSystem.Application.Common.Interfaces;
using AttendanceSystem.Application.Features.Attendance.Commands.ClockIn;
using AttendanceSystem.Application.Features.Attendance.Commands.ClockOut;
using AttendanceSystem.Application.Features.Attendance.Commands.ManualCorrection;
using AttendanceSystem.Domain.Entities;
using AttendanceSystem.Domain.Exceptions;
using AttendanceSystem.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.UnitTests;

public class ClockInHandlerTests
{
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IAttendanceRepository> _attendance = new();
    private readonly Mock<IAuditRepository> _audits = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ITimeService> _time = new();

    private const string TestSource = "https://test/timezone/Europe/Zurich";

    public ClockInHandlerTests()
    {
        _uow.SetupGet(u => u.Employees).Returns(_employees.Object);
        _uow.SetupGet(u => u.Attendance).Returns(_attendance.Object);
        _uow.SetupGet(u => u.Audits).Returns(_audits.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private ClockInCommandHandler ClockInHandler() =>
        new(_uow.Object, _time.Object, NullLogger<ClockInCommandHandler>.Instance);

    private ClockOutCommandHandler ClockOutHandler() =>
        new(_uow.Object, _time.Object, NullLogger<ClockOutCommandHandler>.Instance);

    private ManualCorrectionCommandHandler ManualCorrectionHandler() =>
        new(_uow.Object, NullLogger<ManualCorrectionCommandHandler>.Instance);

    private static Employee NewActiveEmployee() =>
        Employee.Create("Anna Müller", "anna@company.ch", "EMP001", "hash");

    private void SetupTime(DateTimeOffset value) =>
        _time.Setup(t => t.GetCurrentTimeAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(ZurichTime.FromApiResponse(value, TestSource));

    // ----------------------------------------------------------------- ClockIn

    [Fact]
    public async Task ClockIn_Success_WhenEmployeeActiveAndNoOpenSession()
    {
        var emp = NewActiveEmployee();
        var now = new DateTimeOffset(2025, 1, 15, 9, 0, 0, TimeSpan.FromHours(1));
        _employees.Setup(r => r.GetByIdAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        _attendance.Setup(r => r.GetActiveSessionAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync((AttendanceLog?)null);
        SetupTime(now);

        AttendanceLog? added = null;
        _attendance.Setup(r => r.AddAsync(It.IsAny<AttendanceLog>(), It.IsAny<CancellationToken>()))
                   .Callback<AttendanceLog, CancellationToken>((l, _) => added = l)
                   .Returns(Task.CompletedTask);

        var result = await ClockInHandler().Handle(new ClockInCommand(emp.Id), CancellationToken.None);

        result.EmployeeName.Should().Be("Anna Müller");
        result.ClockInUtc.Should().Be(now);
        result.TimeSource.Should().Be(TestSource);
        added.Should().NotBeNull();
        _attendance.Verify(r => r.AddAsync(It.IsAny<AttendanceLog>(), It.IsAny<CancellationToken>()), Times.Once);
        _audits.Verify(r => r.AddAsync(It.Is<AuditLog>(a => a.Action == "ClockIn"), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClockIn_Throws_AlreadyClockedInException_WhenOpenSessionExists()
    {
        var emp = NewActiveEmployee();
        var existing = AttendanceLog.CreateClockIn(emp.Id, new DateTimeOffset(2025, 1, 15, 8, 0, 0, TimeSpan.FromHours(1)), TestSource);
        _employees.Setup(r => r.GetByIdAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        _attendance.Setup(r => r.GetActiveSessionAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var act = async () => await ClockInHandler().Handle(new ClockInCommand(emp.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<AlreadyClockedInException>())
            .Which.ExistingClockInTime.Should().Be(existing.ClockInUtc);
        _time.Verify(t => t.GetCurrentTimeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ClockIn_Throws_EmployeeNotFoundException_WhenEmployeeNotFound()
    {
        _employees.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);

        var act = async () => await ClockInHandler().Handle(new ClockInCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<EmployeeNotFoundException>();
        _time.Verify(t => t.GetCurrentTimeAsync(It.IsAny<CancellationToken>()), Times.Never);
        _attendance.Verify(r => r.AddAsync(It.IsAny<AttendanceLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ClockIn_Throws_EmployeeNotFoundException_WhenEmployeeInactive()
    {
        var emp = NewActiveEmployee();
        emp.Deactivate();
        _employees.Setup(r => r.GetByIdAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(emp);

        var act = async () => await ClockInHandler().Handle(new ClockInCommand(emp.Id), CancellationToken.None);

        await act.Should().ThrowAsync<EmployeeNotFoundException>();
        _attendance.Verify(r => r.AddAsync(It.IsAny<AttendanceLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ClockIn_Throws_TimeServiceUnavailableException_WhenApiDown()
    {
        var emp = NewActiveEmployee();
        _employees.Setup(r => r.GetByIdAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        _attendance.Setup(r => r.GetActiveSessionAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync((AttendanceLog?)null);
        _time.Setup(t => t.GetCurrentTimeAsync(It.IsAny<CancellationToken>()))
             .ThrowsAsync(new TimeServiceUnavailableException("down"));

        var act = async () => await ClockInHandler().Handle(new ClockInCommand(emp.Id), CancellationToken.None);

        await act.Should().ThrowAsync<TimeServiceUnavailableException>();
        // CRITICAL: no attendance record and no save when the time source is unavailable.
        _attendance.Verify(r => r.AddAsync(It.IsAny<AttendanceLog>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ClockIn_UsesTimeFromService_NotFromSystemClock()
    {
        var emp = NewActiveEmployee();
        // A time deliberately far from "now" so a system-clock leak would be obvious.
        var apiTime = new DateTimeOffset(2020, 6, 15, 12, 0, 0, TimeSpan.FromHours(2));
        _employees.Setup(r => r.GetByIdAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        _attendance.Setup(r => r.GetActiveSessionAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync((AttendanceLog?)null);
        SetupTime(apiTime);

        AttendanceLog? added = null;
        _attendance.Setup(r => r.AddAsync(It.IsAny<AttendanceLog>(), It.IsAny<CancellationToken>()))
                   .Callback<AttendanceLog, CancellationToken>((l, _) => added = l)
                   .Returns(Task.CompletedTask);

        var result = await ClockInHandler().Handle(new ClockInCommand(emp.Id), CancellationToken.None);

        added!.ClockInUtc.Should().Be(apiTime);
        added.ClockInSource.Should().Be(TestSource);
        result.ClockInUtc.Should().Be(apiTime);
        _time.Verify(t => t.GetCurrentTimeAsync(It.IsAny<CancellationToken>()), Times.Once);
        _time.VerifyNoOtherCalls();
    }

    // ----------------------------------------------------------------- ClockOut

    [Fact]
    public async Task ClockOut_Success_WhenOpenSessionExists()
    {
        var emp = NewActiveEmployee();
        var clockIn = new DateTimeOffset(2025, 1, 15, 9, 0, 0, TimeSpan.FromHours(1));
        var session = AttendanceLog.CreateClockIn(emp.Id, clockIn, TestSource);
        _employees.Setup(r => r.GetByIdAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        _attendance.Setup(r => r.GetActiveSessionAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        SetupTime(clockIn.AddHours(8));

        var result = await ClockOutHandler().Handle(new ClockOutCommand(emp.Id), CancellationToken.None);

        result.ClockOutUtc.Should().Be(clockIn.AddHours(8));
        result.DurationFormatted.Should().Be("8h 0m");
        _attendance.Verify(r => r.Update(session), Times.Once);
        _audits.Verify(r => r.AddAsync(It.Is<AuditLog>(a => a.Action == "ClockOut"), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClockOut_Throws_NotClockedInException_WhenNoOpenSession()
    {
        var emp = NewActiveEmployee();
        _employees.Setup(r => r.GetByIdAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        _attendance.Setup(r => r.GetActiveSessionAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync((AttendanceLog?)null);

        var act = async () => await ClockOutHandler().Handle(new ClockOutCommand(emp.Id), CancellationToken.None);

        await act.Should().ThrowAsync<NotClockedInException>();
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ClockOut_RecordsCorrectDuration()
    {
        var emp = NewActiveEmployee();
        var clockIn = new DateTimeOffset(2025, 1, 15, 8, 0, 0, TimeSpan.FromHours(1));
        var session = AttendanceLog.CreateClockIn(emp.Id, clockIn, TestSource);
        _employees.Setup(r => r.GetByIdAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        _attendance.Setup(r => r.GetActiveSessionAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        SetupTime(clockIn.Add(new TimeSpan(7, 43, 0)));

        var result = await ClockOutHandler().Handle(new ClockOutCommand(emp.Id), CancellationToken.None);

        result.DurationFormatted.Should().Be("7h 43m");
    }

    // ----------------------------------------------------------------- ManualCorrection

    [Fact]
    public async Task ManualCorrection_WritesAuditTrail()
    {
        var employeeId = Guid.NewGuid();
        var correctorId = Guid.NewGuid();
        var clockIn = new DateTimeOffset(2025, 1, 15, 9, 0, 0, TimeSpan.FromHours(1));
        var log = AttendanceLog.CreateClockIn(employeeId, clockIn, TestSource);
        log.RecordClockOut(clockIn.AddHours(8), TestSource);

        _attendance.Setup(r => r.GetByIdAsync(log.Id, It.IsAny<CancellationToken>())).ReturnsAsync(log);

        var cmd = new ManualCorrectionCommand(
            log.Id, correctorId, clockIn.AddMinutes(5), clockIn.AddHours(8), "Employee forgot to clock in on time");

        var result = await ManualCorrectionHandler().Handle(cmd, CancellationToken.None);

        result.CorrectedByEmployeeId.Should().Be(correctorId);
        log.IsManualCorrection.Should().BeTrue();
        log.CorrectedByEmployeeId.Should().Be(correctorId);
        _attendance.Verify(r => r.Update(log), Times.Once);
        _audits.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a => a.Action == "ManualCorrection" && a.EntityId == log.Id && a.ChangedByEmployeeId == correctorId),
            It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
