namespace BatteryEC.Domain.Entities.Catalog;

public class ProductAttribute
{
    public int AttributeId { get; set; }
    public string AttributeName { get; set; } = string.Empty;
    public string Name { get => AttributeName; set => AttributeName = value; }
    public string AttributeCode { get; set; } = string.Empty;
    public string Code { get => AttributeCode; set => AttributeCode = value; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AttributeValue> Values { get; set; } = new List<AttributeValue>();
}
