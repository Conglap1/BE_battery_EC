using BatteryEC.Domain.Common;

namespace BatteryEC.Domain.Entities.Identity;

public class Role
{
    public int RoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
