using MediatR;

namespace AttendanceSystem.Application.Features.Attendance.Commands.ClockIn;

public record ClockInCommand(Guid EmployeeId, string? Notes = null) : IRequest<ClockInResult>;
