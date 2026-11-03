using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Entities.Identity;

namespace BatteryEC.Domain.Entities.Inventory;

public class InventoryTransaction
{
    public long InventoryTransactionId { get; set; }
    public long ProductVariantId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public int QuantityChange { get; set; }
    public string ReferenceType { get; set; } = string.Empty;
    public long ReferenceId { get; set; }
    public decimal UnitCost { get; set; }
    public string? Note { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ProductVariant ProductVariant { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}
