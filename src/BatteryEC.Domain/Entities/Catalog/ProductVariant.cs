using BatteryEC.Domain.Common;
using BatteryEC.Domain.Entities.Inventory;
using BatteryEC.Domain.Entities.Pos;
using BatteryEC.Domain.Entities.Production;
using BatteryEC.Domain.Entities.Purchasing;
using BatteryEC.Domain.Entities.Sales;
using BatteryEC.Domain.Entities.Services;

namespace BatteryEC.Domain.Entities.Catalog;

public class ProductVariant : BaseEntity, IAggregateRoot
{
    public long ProductVariantId { get => Id; set => Id = value; }
    public long ProductId { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal SellingPrice { get => Price; set => Price = value; }
    public decimal? CostPrice { get; set; }
    public int WeightGrams { get; set; }
    public int WarrantyMonths { get; set; }
    public bool IsBundle { get; set; }
    public bool IsActive { get; set; } = true;

    // Optional convenience properties
    public string? OptionSummary { get; set; }
    public string Unit { get; set; } = "Cái";
    public string? ImageUrl { get; set; }

    public Product Product { get; set; } = null!;
    public BatteryEC.Domain.Entities.Inventory.Inventory? Inventory { get; set; }

    public ICollection<ProductVariantAttribute> VariantAttributes { get; set; } = new List<ProductVariantAttribute>();
    public ICollection<ProductVariantBundle> BundlesAsParent { get; set; } = new List<ProductVariantBundle>();
    public ICollection<ProductVariantBundle> BundlesAsComponent { get; set; } = new List<ProductVariantBundle>();
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();
    public ICollection<AssemblyOrderItem> AssemblyOrderItems { get; set; } = new List<AssemblyOrderItem>();
    public ICollection<PurchaseReceiptItem> PurchaseReceiptItems { get; set; } = new List<PurchaseReceiptItem>();
    public ICollection<RepairTicketItem> RepairTicketItems { get; set; } = new List<RepairTicketItem>();
    public ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();
}
