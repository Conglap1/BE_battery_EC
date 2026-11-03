using BatteryEC.Domain.Entities.Identity;

namespace BatteryEC.Application.Common.Interfaces;

public interface IJwtTokenGenerator
{
    string GenerateAccessToken(User user, IEnumerable<string> roles);
    string GenerateRefreshToken();
}
