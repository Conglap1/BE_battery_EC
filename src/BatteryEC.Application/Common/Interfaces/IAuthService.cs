using BatteryEC.Application.DTOs.Auth;

namespace BatteryEC.Application.Common.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default);
    Task<AuthResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
    Task<AuthResponseDto> RefreshTokenAsync(string? refreshToken, CancellationToken cancellationToken = default);
    Task<UserProfileDto> GetCurrentUserAsync(long userId, CancellationToken cancellationToken = default);
}
