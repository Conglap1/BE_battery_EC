using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BatteryEC.Application.DTOs.Auth;

/// <summary>
/// Request payload for user login.
/// Supports flexible identification via Username, Phone number, or Email.
/// </summary>
public class LoginRequestDto
{
    private string _identifier = string.Empty;

    public string UsernameOrPhoneOrEmail
    {
        get => _identifier;
        set => _identifier = value ?? string.Empty;
    }

    [JsonPropertyName("identifier")]
    public string? Identifier
    {
        get => _identifier;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                _identifier = value.Trim();
        }
    }

    [JsonPropertyName("username")]
    public string? Username
    {
        get => _identifier;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                _identifier = value.Trim();
        }
    }

    [Required(ErrorMessage = "Password is required.")]
    public string Password { get; set; } = string.Empty;

    public string GetResolvedIdentifier()
    {
        return _identifier.Trim();
    }
}

/// <summary>
/// Request payload for user registration.
/// </summary>
public class RegisterRequestDto
{
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 50 characters.")]
    public string? Username { get; set; }

    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(100, ErrorMessage = "Full name cannot exceed 100 characters.")]
    public string FullName { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Invalid email address format.")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Phone number is required.")]
    public string Phone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [MinLength(6, ErrorMessage = "Password must be at least 6 characters long.")]
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Request payload for refresh token via JSON body (Channel B).
/// </summary>
public class RefreshTokenRequestDto
{
    public string? RefreshToken { get; set; }
}

/// <summary>
/// Authentication response containing JWT Access Token, Refresh Token, and identity summary.
/// </summary>
public class AuthResponseDto
{
    public long UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; } = 900; // 15 minutes in seconds
    public List<string> Roles { get; set; } = new();
    public UserDto? User { get; set; }
}

/// <summary>
/// Detailed user profile returned by GET /api/auth/me.
/// </summary>
public class UserProfileDto
{
    public long UserId { get; set; }

    /// <summary>
    /// Alias property for clients querying .id instead of .userId.
    /// </summary>
    public long Id => UserId;

    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public long? CustomerId { get; set; }
    public List<string> Roles { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// General user data transfer object.
/// </summary>
public class UserDto
{
    public long UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string FullName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public long? CustomerId { get; set; }
}
