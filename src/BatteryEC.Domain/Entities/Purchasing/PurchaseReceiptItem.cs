using BatteryEC.Domain.Entities.Catalog;

namespace BatteryEC.Domain.Entities.Purchasing;

public class PurchaseReceiptItem
{
    public long PurchaseReceiptItemId { get; set; }
    public long PurchaseReceiptId { get; set; }
    public long ProductVariantId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }

    public PurchaseReceipt PurchaseReceipt { get; set; } = null!;
    public ProductVariant ProductVariant { get; set; } = null!;
}
