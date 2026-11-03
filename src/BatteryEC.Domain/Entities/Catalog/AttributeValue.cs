namespace BatteryEC.Domain.Entities.Catalog;

public class AttributeValue
{
    public int AttributeValueId { get; set; }
    public int AttributeId { get; set; }
    public string ValueName { get; set; } = string.Empty;
    public string Value { get => ValueName; set => ValueName = value; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ProductAttribute Attribute { get; set; } = null!;
    public ICollection<ProductVariantAttribute> VariantAttributes { get; set; } = new List<ProductVariantAttribute>();
}
