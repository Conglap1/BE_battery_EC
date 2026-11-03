using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Entities.Operations;

namespace BatteryEC.Domain.Entities.Sales;

public class OrderItem
{
    public long OrderItemId { get; set; }
    public long OrderId { get; set; }
    public long ProductVariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal? CostPrice { get; set; }
    public int WarrantyMonths { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }

    public Order Order { get; set; } = null!;
    public ProductVariant ProductVariant { get; set; } = null!;
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}
