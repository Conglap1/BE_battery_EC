using System.Security.Claims;
using BatteryEC.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace BatteryEC.Infrastructure.Services;

/// <summary>
/// Implements ICurrentUserService by extracting authenticated user claims from IHttpContextAccessor.
/// Thread-safe and scoped per HTTP request.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public long? UserId
    {
        get
        {
            var user = User;
            if (user == null) return null;

            var claimValue = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value
                ?? user.FindFirst("id")?.Value
                ?? user.FindFirst("nameid")?.Value;

            return long.TryParse(claimValue, out var id) ? id : null;
        }
    }

    public string? UserName
    {
        get
        {
            var user = User;
            if (user == null) return null;

            return user.FindFirst(ClaimTypes.Name)?.Value
                ?? user.FindFirst("name")?.Value
                ?? user.FindFirst("unique_name")?.Value
                ?? user.Identity?.Name;
        }
    }

    public string? Email
    {
        get
        {
            var user = User;
            if (user == null) return null;

            return user.FindFirst(ClaimTypes.Email)?.Value
                ?? user.FindFirst("email")?.Value;
        }
    }

    public IReadOnlyList<string> Roles
    {
        get
        {
            var user = User;
            if (user == null) return Array.Empty<string>();

            return user.FindAll(c => c.Type == ClaimTypes.Role || c.Type == "role" || c.Type == "roles")
                       .Select(c => c.Value)
                       .Where(r => !string.IsNullOrWhiteSpace(r))
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .ToList()
                       .AsReadOnly();
        }
    }

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public bool IsInRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role)) return false;
        var user = User;
        if (user == null) return false;

        return user.IsInRole(role) || Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }
}
