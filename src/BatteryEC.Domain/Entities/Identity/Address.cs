namespace BatteryEC.Domain.Entities.Identity;

public class Address
{
    public long AddressId { get; set; }
    public long UserId { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string RecipientPhone { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string? District { get; set; }
    public string? Ward { get; set; }
    public string AddressLine { get; set; } = string.Empty;
    public string? PostalCode { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}
