namespace BatteryEC.Application.DTOs.Inventory;

public class InventoryDto
{
    public long ProductVariantId { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public int QuantityOnHand { get; set; }
    public int QuantityReserved { get; set; }
    public int AvailableStock => QuantityOnHand - QuantityReserved;
    public int ReorderLevel { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class StockAdjustmentDto
{
    public long ProductVariantId { get; set; }
    public int QuantityChange { get; set; }
    public string Reason { get; set; } = string.Empty;
    public decimal? UnitCost { get; set; }
}

public class InventoryTransactionDto
{
    public long InventoryTransactionId { get; set; }
    public long ProductVariantId { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public int QuantityChange { get; set; }
    public string? ReferenceType { get; set; }
    public long? ReferenceId { get; set; }
    public decimal? UnitCost { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}
