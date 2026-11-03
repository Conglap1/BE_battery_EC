using BatteryEC.Domain.Common;
using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Entities.Identity;

namespace BatteryEC.Domain.Entities.Production;

public class AssemblyOrder : IAggregateRoot
{
    public long AssemblyOrderId { get; set; }
    public string AssemblyCode { get; set; } = string.Empty;
    public long BundleVariantId { get; set; }
    public int QuantityProduced { get; set; }
    public decimal TotalCost { get; set; }
    public decimal UnitCost { get; set; }
    public string Status { get; set; } = "Completed";
    public string? Note { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime AssembledAt { get; set; } = DateTime.UtcNow;

    public ProductVariant BundleVariant { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public ICollection<AssemblyOrderItem> Items { get; set; } = new List<AssemblyOrderItem>();
}
