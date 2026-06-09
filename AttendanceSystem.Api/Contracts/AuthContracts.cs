namespace AttendanceSystem.Api.Contracts;

public record LoginRequest(string Email, string Password);

public record RefreshRequest(string RefreshToken);

public record EmployeeSummary(Guid Id, string Name, string Role, string BadgeNumber);

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    EmployeeSummary Employee);

public record RefreshResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
