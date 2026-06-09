using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Common.Interfaces;
using AttendanceSystem.Domain.Exceptions;
using MediatR;

namespace AttendanceSystem.Application.Features.Attendance.Queries.GetStatus;

public class GetMyStatusQueryHandler : IRequestHandler<GetMyStatusQuery, AttendanceStatusDto>
{
    private readonly IUnitOfWork _uow;
    private readonly ITimeService _timeService;

    public GetMyStatusQueryHandler(IUnitOfWork uow, ITimeService timeService)
    {
        _uow = uow;
        _timeService = timeService;
    }

    public async Task<AttendanceStatusDto> Handle(GetMyStatusQuery request, CancellationToken ct)
    {
        var session = await _uow.Attendance.GetActiveSessionAsync(request.EmployeeId, ct);

        // Current Zurich time comes from the external service (never the server clock).
        // Best-effort: if the time service is down, status still returns so the UI can load —
        // only the actual clock-in/out operations hard-fail with 503.
        DateTimeOffset? now = null;
        try
        {
            now = (await _timeService.GetCurrentTimeAsync(ct)).Value;
        }
        catch (TimeServiceUnavailableException)
        {
            now = null;
        }

        if (session is null)
            return new AttendanceStatusDto { IsClockedIn = false, CurrentZurichTime = now };

        return new AttendanceStatusDto
        {
            IsClockedIn = true,
            ClockInTime = session.ClockInUtc,
            DurationSoFar = now.HasValue ? DurationFormatter.Format(now.Value - session.ClockInUtc) : null,
            LogId = session.Id,
            CurrentZurichTime = now
        };
    }
}
