using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.Common.Models;
using BatteryEC.Domain.Entities.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BatteryEC.Infrastructure.Security;

/// <summary>
/// Implements JWT Access Token generation using HMAC-SHA256 with 15-minute lifetime,
/// and 64-byte secure random Refresh Token generation.
/// </summary>
public class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtSettings _jwtSettings;

    public JwtTokenGenerator(IOptions<JwtSettings> jwtOptions)
    {
        _jwtSettings = jwtOptions?.Value
            ?? throw new ArgumentNullException(nameof(jwtOptions), "JwtSettings must not be null.");

        if (string.IsNullOrWhiteSpace(_jwtSettings.SecretKey))
        {
            throw new InvalidOperationException("JWT SecretKey is not configured in appsettings.");
        }
    }

    public string GenerateAccessToken(User user, IEnumerable<string> roles)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        byte[] key = Encoding.UTF8.GetBytes(_jwtSettings.SecretKey);

        if (key.Length < 32)
        {
            throw new InvalidOperationException("JWT SecretKey must be at least 256 bits (32 bytes) long.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, !string.IsNullOrWhiteSpace(user.Username) ? user.Username : user.FullName),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (!string.IsNullOrEmpty(user.Phone))
        {
            claims.Add(new Claim("phone", user.Phone));
            claims.Add(new Claim(ClaimTypes.MobilePhone, user.Phone));
        }

        foreach (var role in roles)
        {
            if (!string.IsNullOrWhiteSpace(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes),
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        byte[] randomBytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(randomBytes);
    }
}
