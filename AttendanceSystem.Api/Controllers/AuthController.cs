using AttendanceSystem.Api.Configuration;
using AttendanceSystem.Api.Contracts;
using AttendanceSystem.Api.Services;
using AttendanceSystem.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using BCryptNet = BCrypt.Net.BCrypt;

namespace AttendanceSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IUnitOfWork _uow;
    private readonly TokenService _tokens;
    private readonly JwtSettings _jwt;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IUnitOfWork uow, TokenService tokens, IOptions<JwtSettings> jwt, ILogger<AuthController> logger)
    {
        _uow = uow;
        _tokens = tokens;
        _jwt = jwt.Value;
        _logger = logger;
    }

    /// <summary>Authenticates an employee and returns access + refresh tokens.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var employee = await _uow.Employees.GetByEmailAsync(request.Email, ct);

        // Generic failure message — never reveal which field was wrong.
        if (employee is null || !employee.IsActive || !BCryptNet.Verify(request.Password, employee.PasswordHash))
        {
            _logger.LogWarning("Failed login attempt for {Email}", request.Email);
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var accessToken = _tokens.GenerateAccessToken(employee);
        var rawRefresh = _tokens.GenerateRefreshToken();

        // Persist only the HASH of the refresh token.
        employee.UpdateRefreshToken(_tokens.HashRefreshToken(rawRefresh), _tokens.RefreshTokenExpiry);
        _uow.Employees.Update(employee);
        await _uow.SaveChangesAsync(ct);

        var response = new LoginResponse(
            accessToken,
            // Composite token: the employee id lets /refresh locate the employee; the random part is verified against the stored hash.
            $"{employee.Id}:{rawRefresh}",
            _tokens.AccessTokenExpiry,
            new EmployeeSummary(employee.Id, employee.FullName, employee.Role, employee.BadgeNumber));

        return Ok(response);
    }

    /// <summary>Exchanges a valid refresh token for a new access + refresh token pair (rotation).</summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(RefreshResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var parts = request.RefreshToken.Split(':', 2);
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var employeeId))
            return Unauthorized(new { message = "Invalid refresh token." });

        var employee = await _uow.Employees.GetByIdAsync(employeeId, ct);
        if (employee is null || !_tokens.ValidateRefreshToken(parts[1], employee))
            return Unauthorized(new { message = "Invalid or expired refresh token." });

        var accessToken = _tokens.GenerateAccessToken(employee);
        var rawRefresh = _tokens.GenerateRefreshToken();
        employee.UpdateRefreshToken(_tokens.HashRefreshToken(rawRefresh), _tokens.RefreshTokenExpiry);
        _uow.Employees.Update(employee);
        await _uow.SaveChangesAsync(ct);

        return Ok(new RefreshResponse(accessToken, $"{employee.Id}:{rawRefresh}", _tokens.AccessTokenExpiry));
    }

    /// <summary>Clears the caller's refresh token, invalidating future refreshes.</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        if (sub is null || !Guid.TryParse(sub, out var employeeId))
            return Unauthorized();

        var employee = await _uow.Employees.GetByIdAsync(employeeId, ct);
        if (employee is not null)
        {
            employee.ClearRefreshToken();
            _uow.Employees.Update(employee);
            await _uow.SaveChangesAsync(ct);
        }

        return NoContent();
    }
}
