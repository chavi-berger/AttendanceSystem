using MediatR;

namespace AttendanceSystem.Application.Features.Attendance.Queries.GetActiveEmployees;

public record GetActiveEmployeesQuery : IRequest<IEnumerable<ActiveEmployeeDto>>;
