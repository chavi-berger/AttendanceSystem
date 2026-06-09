using System.Security.Claims;
using AttendanceSystem.Api.Contracts;
using AttendanceSystem.Api.Extensions;
using AttendanceSystem.Application.Features.Attendance.Commands.ClockIn;
using AttendanceSystem.Application.Features.Attendance.Commands.ClockOut;
using AttendanceSystem.Application.Features.Attendance.Commands.ManualCorrection;
using AttendanceSystem.Application.Features.Attendance.Queries.GetActiveEmployees;
using AttendanceSystem.Application.Features.Attendance.Queries.GetHistory;
using AttendanceSystem.Application.Features.Attendance.Queries.GetStatus;
using AttendanceSystem.Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AttendanceSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AttendanceController : ControllerBase
{
    private readonly ISender _mediator;

    public AttendanceController(ISender mediator) => _mediator = mediator;

    /// <summary>Clock in the current employee using the official Europe/Zurich time.</summary>
    [HttpPost("clock-in")]
    [EnableRateLimiting(ApiServiceExtensions.ClockOperationsPolicy)]
    [ProducesResponseType(typeof(ClockInResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ClockIn([FromBody] ClockRequest? body, CancellationToken ct)
    {
        var result = await _mediator.Send(new ClockInCommand(GetCurrentEmployeeId(), body?.Notes), ct);
        return Ok(result);
    }

    /// <summary>Clock out the current employee using the official Europe/Zurich time.</summary>
    [HttpPost("clock-out")]
    [EnableRateLimiting(ApiServiceExtensions.ClockOperationsPolicy)]
    [ProducesResponseType(typeof(ClockOutResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ClockOut([FromBody] ClockRequest? body, CancellationToken ct)
    {
        // Managers/Admins may force clock-out for another employee by supplying employeeId.
        // Regular employees can only clock themselves out (any supplied employeeId is ignored).
        var targetId = GetCurrentEmployeeId();
        if (body?.EmployeeId is Guid requested && requested != Guid.Empty && IsManagerOrAdmin())
            targetId = requested;

        var result = await _mediator.Send(new ClockOutCommand(targetId, body?.Notes), ct);
        return Ok(result);
    }

    /// <summary>Current clock-in status of the caller.</summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(AttendanceStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMyStatusQuery(GetCurrentEmployeeId()), ct);
        return Ok(result);
    }

    /// <summary>
    /// Attendance history. Employees see only their own; Managers/Admins may pass ?employeeId= to view others.
    /// </summary>
    [HttpGet("history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> History(
        [FromQuery] Guid? employeeId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var currentId = GetCurrentEmployeeId();
        var targetId = employeeId ?? currentId;

        if (targetId != currentId && !IsManagerOrAdmin())
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "You are not allowed to view another employee's history." });

        var result = await _mediator.Send(new GetAttendanceHistoryQuery(targetId, from, to, page, pageSize), ct);
        return Ok(result);
    }

    /// <summary>All currently open sessions (admin dashboard).</summary>
    [HttpGet("active")]
    [Authorize(Roles = Roles.ManagerOrAdmin)]
    [ProducesResponseType(typeof(IEnumerable<ActiveEmployeeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Active(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetActiveEmployeesQuery(), ct);
        return Ok(result);
    }

    /// <summary>Manually correct an attendance log (admin only). Writes a full audit trail.</summary>
    [HttpPut("{logId:guid}/correct")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(ManualCorrectionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Correct(Guid logId, [FromBody] ManualCorrectionRequest body, CancellationToken ct)
    {
        var command = new ManualCorrectionCommand(
            logId, GetCurrentEmployeeId(), body.NewClockIn, body.NewClockOut, body.Reason);
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    private Guid GetCurrentEmployeeId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? throw new UnauthorizedAccessException("Invalid token");
        return Guid.Parse(sub);
    }

    private bool IsManagerOrAdmin() =>
        User.IsInRole(Roles.Manager) || User.IsInRole(Roles.Admin);
}
