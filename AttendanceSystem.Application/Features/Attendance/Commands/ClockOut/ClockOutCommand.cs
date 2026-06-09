using MediatR;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ClockOut;

public record ClockOutCommand(Guid EmployeeId, string? Notes = null) : IRequest<ClockOutResult>;
