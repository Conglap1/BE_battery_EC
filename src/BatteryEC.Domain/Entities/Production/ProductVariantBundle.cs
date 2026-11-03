using BatteryEC.Domain.Entities.Catalog;

namespace BatteryEC.Domain.Entities.Production;

public class ProductVariantBundle
{
    public long BundleVariantId { get; set; }
    public long ComponentVariantId { get; set; }
    public int Quantity { get; set; }

    public ProductVariant BundleVariant { get; set; } = null!;
    public ProductVariant ComponentVariant { get; set; } = null!;
}
