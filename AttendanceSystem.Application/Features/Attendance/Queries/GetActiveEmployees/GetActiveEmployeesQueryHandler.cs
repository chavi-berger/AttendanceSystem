using AttendanceSystem.Application.Common;
using AttendanceSystem.Application.Common.Interfaces;
using MediatR;

namespace AttendanceSystem.Application.Features.Attendance.Queries.GetActiveEmployees;

public class GetActiveEmployeesQueryHandler
    : IRequestHandler<GetActiveEmployeesQuery, IEnumerable<ActiveEmployeeDto>>
{
    private readonly IUnitOfWork _uow;
    private readonly ITimeService _timeService;

    public GetActiveEmployeesQueryHandler(IUnitOfWork uow, ITimeService timeService)
    {
        _uow = uow;
        _timeService = timeService;
    }

    public async Task<IEnumerable<ActiveEmployeeDto>> Handle(
        GetActiveEmployeesQuery request, CancellationToken ct)
    {
        var sessions = await _uow.Attendance.GetAllActiveSessionsAsync(ct);

        // "Now" comes from the time service, never the server clock.
        var now = (await _timeService.GetCurrentTimeAsync(ct)).Value;

        var result = new List<ActiveEmployeeDto>();
        foreach (var session in sessions)
        {
            var employee = await _uow.Employees.GetByIdAsync(session.EmployeeId, ct);
            if (employee is null) continue; // inactive/removed employee — skip

            result.Add(new ActiveEmployeeDto
            {
                EmployeeId = employee.Id,
                EmployeeName = employee.FullName,
                BadgeNumber = employee.BadgeNumber,
                ClockInUtc = session.ClockInUtc,
                DurationSoFar = DurationFormatter.Format(now - session.ClockInUtc),
                LogId = session.Id
            });
        }

        return result;
    }
}
