using MediatR;

namespace AttendanceSystem.Application.Features.Attendance.Queries.GetStatus;

public record GetMyStatusQuery(Guid EmployeeId) : IRequest<AttendanceStatusDto>;
