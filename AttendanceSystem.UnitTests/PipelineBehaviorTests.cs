using AttendanceSystem.Application;
using AttendanceSystem.Application.Common.Interfaces;
using AttendanceSystem.Application.Features.Attendance.Commands.ClockIn;
using AttendanceSystem.Domain.Entities;
using AttendanceSystem.Domain.Exceptions;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AttendanceSystem.UnitTests;

public class PipelineBehaviorTests
{
    private static IMediator BuildMediator(IUnitOfWork? uow = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddApplication(); // registers MediatR, validators, Validation + Logging behaviors
        services.AddSingleton(uow ?? Mock.Of<IUnitOfWork>());
        services.AddSingleton(Mock.Of<ITimeService>());
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task ValidationBehavior_CollectsAllErrors_NotJustFirst()
    {
        var mediator = BuildMediator();

        // Empty EmployeeId (rule 1) AND a 501-char Notes (rule 2) → two distinct failures.
        var invalid = new ClockInCommand(Guid.Empty, new string('x', 501));

        var act = async () => await mediator.Send(invalid);

        var ex = (await act.Should().ThrowAsync<ValidationException>()).Which;
        ex.Errors.Should().HaveCount(2, "the behavior must aggregate all validation errors, not short-circuit");
        ex.Errors.Select(e => e.PropertyName)
          .Should().Contain(new[] { nameof(ClockInCommand.EmployeeId), nameof(ClockInCommand.Notes) });
    }

    [Fact]
    public async Task Pipeline_PassesValidRequestThroughToHandler()
    {
        // Valid command → validation passes → handler runs and (with no employee found) throws
        // EmployeeNotFoundException. That proves the pipeline let a valid request through.
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((Employee?)null);
        var uow = new Mock<IUnitOfWork>();
        uow.SetupGet(u => u.Employees).Returns(employees.Object);

        var mediator = BuildMediator(uow.Object);

        var act = async () => await mediator.Send(new ClockInCommand(Guid.NewGuid()));

        await act.Should().ThrowAsync<EmployeeNotFoundException>();
    }
}
