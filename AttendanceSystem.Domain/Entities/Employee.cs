using System;
using System.Collections.Generic;

namespace AttendanceSystem.Domain.Entities;

public class Employee
{
    public Guid Id { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string BadgeNumber { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string Role { get; private set; } = "Employee"; // Employee | Manager | Admin
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastModifiedAt { get; private set; }
    public string? RefreshToken { get; private set; }
    public DateTimeOffset? RefreshTokenExpiry { get; private set; }

    // Navigation
    private readonly List<AttendanceLog> _attendanceLogs = new();
    public IReadOnlyCollection<AttendanceLog> AttendanceLogs => _attendanceLogs.AsReadOnly();

    // EF Core constructor
    private Employee() { }

    public static Employee Create(string fullName, string email, string badgeNumber, string passwordHash, string role = "Employee")
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new ArgumentException("FullName is required", nameof(fullName));
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required", nameof(email));
        if (string.IsNullOrWhiteSpace(badgeNumber)) throw new ArgumentException("BadgeNumber is required", nameof(badgeNumber));
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("PasswordHash is required", nameof(passwordHash));

        return new Employee
        {
            Id = Guid.NewGuid(),
            FullName = fullName.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            BadgeNumber = badgeNumber.Trim().ToUpperInvariant(),
            PasswordHash = passwordHash,
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Deactivate() { IsActive = false; LastModifiedAt = DateTimeOffset.UtcNow; }
    public void Activate()   { IsActive = true;  LastModifiedAt = DateTimeOffset.UtcNow; }

    public void ChangeRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException("Role is required", nameof(role));
        Role = role;
        LastModifiedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateRefreshToken(string token, DateTimeOffset expiry)
    {
        RefreshToken = token;
        RefreshTokenExpiry = expiry;
        LastModifiedAt = DateTimeOffset.UtcNow;
    }

    public void ClearRefreshToken()
    {
        RefreshToken = null;
        RefreshTokenExpiry = null;
        LastModifiedAt = DateTimeOffset.UtcNow;
    }

    public bool HasValidRefreshToken(string token) =>
        RefreshToken == token && RefreshTokenExpiry > DateTimeOffset.UtcNow;
}
