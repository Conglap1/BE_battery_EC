using BatteryEC.Domain.Entities.Catalog;

namespace BatteryEC.Domain.Entities.Production;

public class AssemblyOrderItem
{
    public long AssemblyOrderItemId { get; set; }
    public long AssemblyOrderId { get; set; }
    public long ComponentVariantId { get; set; }
    public int QuantityConsumed { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }

    public AssemblyOrder AssemblyOrder { get; set; } = null!;
    public ProductVariant ComponentVariant { get; set; } = null!;
}
