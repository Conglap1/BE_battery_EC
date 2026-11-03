namespace BatteryEC.Domain.Entities.Catalog;

public class ProductVariantAttribute
{
    public long ProductVariantId { get; set; }
    public int AttributeValueId { get; set; }

    public ProductVariant ProductVariant { get; set; } = null!;
    public AttributeValue AttributeValue { get; set; } = null!;
}
