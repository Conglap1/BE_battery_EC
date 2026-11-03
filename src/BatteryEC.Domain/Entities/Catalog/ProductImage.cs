namespace BatteryEC.Domain.Entities.Catalog;

public class ProductImage
{
    public long ImageId { get; set; }
    public long ProductImageId { get => ImageId; set => ImageId = value; }
    public long ProductId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public bool IsThumbnail { get; set; }
    public bool IsPrimary { get => IsThumbnail; set => IsThumbnail = value; }
    public int DisplayOrder { get; set; }
    public int SortOrder { get => DisplayOrder; set => DisplayOrder = value; }
    public string? AltText { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Product Product { get; set; } = null!;
}
