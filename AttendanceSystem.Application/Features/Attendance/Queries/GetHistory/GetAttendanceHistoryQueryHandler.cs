using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Common.Interfaces;
using AttendanceSystem.Application.Common.Models;
using MediatR;

namespace AttendanceSystem.Application.Features.Attendance.Queries.GetHistory;

public class GetAttendanceHistoryQueryHandler
    : IRequestHandler<GetAttendanceHistoryQuery, PagedResult<AttendanceHistoryDto>>
{
    private readonly IUnitOfWork _uow;

    public GetAttendanceHistoryQueryHandler(IUnitOfWork uow) => _uow = uow;

    public async Task<PagedResult<AttendanceHistoryDto>> Handle(
        GetAttendanceHistoryQuery request, CancellationToken ct)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 20 : request.PageSize;

        var logs = await _uow.Attendance.GetHistoryAsync(
            request.EmployeeId, request.From, request.To, page, pageSize, ct);

        var totalCount = await _uow.Attendance.GetHistoryCountAsync(
            request.EmployeeId, request.From, request.To, ct);

        var items = logs.Select(log => new AttendanceHistoryDto
        {
            Id = log.Id,
            ClockInUtc = log.ClockInUtc,
            ClockOutUtc = log.ClockOutUtc,
            DurationFormatted = DurationFormatter.FormatDuration(log.ClockInUtc, log.ClockOutUtc),
            CrossesMidnight = DurationFormatter.CrossesMidnight(log.ClockInUtc, log.ClockOutUtc),
            IsOpen = log.IsOpen,
            IsManualCorrection = log.IsManualCorrection,
            IsAutoTimeout = log.IsAutoTimeout,
            ClockInSource = log.ClockInSource
        }).ToList();

        return new PagedResult<AttendanceHistoryDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }
}
