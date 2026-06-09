namespace AttendanceSystem.Api.Contracts;

public record EmployeeListItemDto(Guid Id, string FullName, string Email, string BadgeNumber, string Role, bool IsActive);

public record CreateEmployeeRequest(string FullName, string Email, string BadgeNumber, string Password, string Role);

public record ChangeRoleRequest(string Role);
