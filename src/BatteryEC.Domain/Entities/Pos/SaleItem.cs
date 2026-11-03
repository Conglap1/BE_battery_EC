using BatteryEC.Domain.Entities.Catalog;

namespace BatteryEC.Domain.Entities.Pos;

public class SaleItem
{
    public long SaleItemId { get; set; }
    public long SaleId { get; set; }
    public long ProductVariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal CostPrice { get; set; }
    public int WarrantyMonths { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }

    public Sale Sale { get; set; } = null!;
    public ProductVariant ProductVariant { get; set; } = null!;
}
