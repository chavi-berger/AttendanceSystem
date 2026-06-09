using System.Security.Claims;
using System.Text.Json;
using AttendanceSystem.Api.Contracts;
using AttendanceSystem.Application.Common.Interfaces;
using AttendanceSystem.Domain.Constants;
using AttendanceSystem.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BCryptNet = BCrypt.Net.BCrypt;

namespace AttendanceSystem.Api.Controllers;

/// <summary>
/// Admin user-management. Listing is available to Managers/Admins (so Managers can pick an
/// employee to view history); all mutations require the Admin role. Every mutation is audited.
/// </summary>
[ApiController]
[Route("api/employees")]
[Authorize]
public class EmployeeController : ControllerBase
{
    private static readonly string[] AllowedRoles = { Roles.Employee, Roles.Manager, Roles.Admin };

    private readonly IUnitOfWork _uow;
    private readonly ILogger<EmployeeController> _logger;

    public EmployeeController(IUnitOfWork uow, ILogger<EmployeeController> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    /// <summary>List all employees (including deactivated). Managers and Admins.</summary>
    [HttpGet]
    [Authorize(Roles = Roles.ManagerOrAdmin)]
    [ProducesResponseType(typeof(IEnumerable<EmployeeListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var employees = await _uow.Employees.GetAllAsync(ct);
        return Ok(employees.Select(ToDto));
    }

    /// <summary>Create a new employee (Admin only).</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(EmployeeListItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeRequest req, CancellationToken ct)
    {
        if (!IsValidRole(req.Role))
            return BadRequest(new { message = "Role must be Employee, Manager, or Admin." });
        if (string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { message = "Password is required." });

        // Uniqueness check across ALL employees (including deactivated ones).
        var all = (await _uow.Employees.GetAllAsync(ct)).ToList();
        var email = req.Email.Trim().ToLowerInvariant();
        var badge = req.BadgeNumber.Trim().ToUpperInvariant();
        if (all.Any(e => e.Email == email))
            return Conflict(new { message = "An employee with this email already exists." });
        if (all.Any(e => e.BadgeNumber == badge))
            return Conflict(new { message = "An employee with this badge number already exists." });

        var employee = Employee.Create(req.FullName, req.Email, req.BadgeNumber, BCryptNet.HashPassword(req.Password), req.Role);
        await _uow.Employees.AddAsync(employee, ct);
        await _uow.Audits.AddAsync(AuditLog.Create("Employee", employee.Id, "Create",
            null,
            JsonSerializer.Serialize(new { employee.FullName, employee.Email, employee.BadgeNumber, employee.Role }),
            CurrentUserId()), ct);
        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Admin {AdminId} created employee {EmployeeId} ({Email})", CurrentUserId(), employee.Id, employee.Email);
        return CreatedAtAction(nameof(GetAll), new { id = employee.Id }, ToDto(employee));
    }

    /// <summary>Change an employee's role (Admin only).</summary>
    [HttpPut("{id:guid}/role")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(EmployeeListItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangeRole(Guid id, [FromBody] ChangeRoleRequest req, CancellationToken ct)
    {
        if (!IsValidRole(req.Role))
            return BadRequest(new { message = "Role must be Employee, Manager, or Admin." });

        var employee = await _uow.Employees.GetByIdForUpdateAsync(id, ct);
        if (employee is null) return NotFound(new { message = "Employee not found." });

        var oldRole = employee.Role;
        employee.ChangeRole(req.Role);
        _uow.Employees.Update(employee);
        await _uow.Audits.AddAsync(AuditLog.Create("Employee", employee.Id, "RoleChange",
            JsonSerializer.Serialize(new { Role = oldRole }),
            JsonSerializer.Serialize(new { employee.Role }),
            CurrentUserId()), ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ToDto(employee));
    }

    /// <summary>Deactivate (soft-delete) an employee (Admin only).</summary>
    [HttpPut("{id:guid}/deactivate")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(EmployeeListItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var employee = await _uow.Employees.GetByIdForUpdateAsync(id, ct);
        if (employee is null) return NotFound(new { message = "Employee not found." });

        employee.Deactivate();
        _uow.Employees.Update(employee);
        await _uow.Audits.AddAsync(AuditLog.Create("Employee", employee.Id, "Deactivate",
            JsonSerializer.Serialize(new { IsActive = true }),
            JsonSerializer.Serialize(new { IsActive = false }),
            CurrentUserId()), ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ToDto(employee));
    }

    /// <summary>Reactivate a deactivated employee (Admin only).</summary>
    [HttpPut("{id:guid}/activate")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(typeof(EmployeeListItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        var employee = await _uow.Employees.GetByIdForUpdateAsync(id, ct);
        if (employee is null) return NotFound(new { message = "Employee not found." });

        employee.Activate();
        _uow.Employees.Update(employee);
        await _uow.Audits.AddAsync(AuditLog.Create("Employee", employee.Id, "Activate",
            JsonSerializer.Serialize(new { IsActive = false }),
            JsonSerializer.Serialize(new { IsActive = true }),
            CurrentUserId()), ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ToDto(employee));
    }

    private static EmployeeListItemDto ToDto(Employee e) =>
        new(e.Id, e.FullName, e.Email, e.BadgeNumber, e.Role, e.IsActive);

    private static bool IsValidRole(string role) => AllowedRoles.Contains(role);

    private Guid CurrentUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
