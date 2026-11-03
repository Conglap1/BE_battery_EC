namespace BatteryEC.Application.Common.Interfaces;

public interface ICurrentUserService
{
    long? UserId { get; }
    string? UserName { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
}
