using BatteryEC.Domain.Common;
using BatteryEC.Domain.Entities.Operations;

namespace BatteryEC.Domain.Entities.Catalog;

public class Product : BaseEntity, IAggregateRoot
{
    public long ProductId { get => Id; set => Id = value; }
    public int CategoryId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Name { get => ProductName; set => ProductName = value; }
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Brand { get; set; }
    public string? ShortDescription { get; set; }
    public bool IsPublished { get => IsActive; set => IsActive = value; }
    public bool IsFeatured { get; set; }
    public bool IsDeleted { get; set; }

    public Category Category { get; set; } = null!;
    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}
