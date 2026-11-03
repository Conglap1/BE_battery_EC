namespace BatteryEC.Domain.Entities.Catalog;

public class Category
{
    public int CategoryId { get; set; }
    public int? ParentCategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Name { get => CategoryName; set => CategoryName = value; }
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public int SortOrder { get => DisplayOrder; set => DisplayOrder = value; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Category? ParentCategory { get; set; }
    public ICollection<Category> SubCategories { get; set; } = new List<Category>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
