using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AttendanceSystem.Api.Configuration;
using AttendanceSystem.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AttendanceSystem.Api.Services;

public class TokenService
{
    private readonly JwtSettings _jwt;

    public TokenService(IOptions<JwtSettings> jwt) => _jwt = jwt.Value;

    public DateTimeOffset AccessTokenExpiry => DateTimeOffset.UtcNow.AddMinutes(_jwt.AccessTokenExpiryMinutes);
    public DateTimeOffset RefreshTokenExpiry => DateTimeOffset.UtcNow.AddDays(_jwt.RefreshTokenExpiryDays);

    /// <summary>Creates a signed JWT access token for the given employee (15 min lifetime).</summary>
    public string GenerateAccessToken(Employee employee)
    {
        // Claim types are emitted exactly as named (inbound mapping is disabled in JwtBearer),
        // so ClaimTypes.NameIdentifier / ClaimTypes.Role survive verbatim.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, employee.Id.ToString()),
            new(ClaimTypes.NameIdentifier, employee.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("name", employee.FullName),
            new(ClaimTypes.Name, employee.FullName),
            new("email", employee.Email),
            new("role", employee.Role),
            new(ClaimTypes.Role, employee.Role),
            new("badge", employee.BadgeNumber),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_jwt.AccessTokenExpiryMinutes), // token lifetime is a system concern
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>Cryptographically random 64-byte refresh token (base64).</summary>
    public string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    /// <summary>SHA-256 hash of a refresh token — only the hash is persisted, never the plaintext.</summary>
    public string HashRefreshToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>True when the supplied raw refresh token matches the (hashed) one stored on the employee and is unexpired.</summary>
    public bool ValidateRefreshToken(string token, Employee employee) =>
        employee.HasValidRefreshToken(HashRefreshToken(token));
}
