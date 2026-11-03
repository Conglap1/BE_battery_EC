using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Entities.Identity;
using BatteryEC.Domain.Entities.Sales;

namespace BatteryEC.Domain.Entities.Operations;

public class Review
{
    public long ReviewId { get; set; }
    public long UserId { get; set; }
    public long ProductId { get; set; }
    public long OrderItemId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public OrderItem OrderItem { get; set; } = null!;
}
