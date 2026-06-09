using AttendanceSystem.Application.Common.Models;
using MediatR;

namespace AttendanceSystem.Application.Features.Attendance.Queries.GetHistory;

public record GetAttendanceHistoryQuery(
    Guid EmployeeId,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 20
) : IRequest<PagedResult<AttendanceHistoryDto>>;
