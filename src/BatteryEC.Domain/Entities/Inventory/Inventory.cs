using BatteryEC.Domain.Common;
using BatteryEC.Domain.Entities.Catalog;

namespace BatteryEC.Domain.Entities.Inventory;

public class Inventory : IAggregateRoot
{
    public long ProductVariantId { get; set; }
    public int QuantityOnHand { get; set; }
    public int QuantityReserved { get; set; }
    public int ReorderLevel { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int Version { get; set; } = 1;

    public int AvailableStock => QuantityOnHand - QuantityReserved;

    public ProductVariant ProductVariant { get; set; } = null!;
}
