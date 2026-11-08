namespace BatteryEC.Application.DTOs.Catalog;

public class CategoryDto
{
    public int CategoryId { get; set; }
    public int? ParentCategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// Public-facing product catalog DTO.
/// Strictly excludes sensitive commercial data such as CostPrice and profit margins.
/// </summary>
public class ProductDto
{
    public long ProductId { get; set; }
    public int CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? Brand { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public bool IsPublished { get; set; }
    public bool IsFeatured { get; set; }
    public List<ProductVariantDto> Variants { get; set; } = new();
}

/// <summary>
/// Public-facing variant DTO.
/// CostPrice is omitted to prevent margin and supplier cost disclosure to unauthorized clients.
/// </summary>
public class ProductVariantDto
{
    public long ProductVariantId { get; set; }
    public long ProductId { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public string? OptionSummary { get; set; }
    public string Unit { get; set; } = "Cái";
    public int WeightGrams { get; set; }
    public string? ImageUrl { get; set; }
    public decimal SellingPrice { get; set; }
    public int WarrantyMonths { get; set; }
    public bool IsActive { get; set; }
    public int? AvailableStock { get; set; }
}

/// <summary>
/// Internal administrative product DTO for privileged back-office operations.
/// </summary>
public class AdminProductDto
{
    public long ProductId { get; set; }
    public int CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? Brand { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public bool IsPublished { get; set; }
    public bool IsFeatured { get; set; }
    public List<AdminProductVariantDto> Variants { get; set; } = new();
}

/// <summary>
/// Internal administrative variant DTO containing full commercial data including CostPrice.
/// </summary>
public class AdminProductVariantDto
{
    public long ProductVariantId { get; set; }
    public long ProductId { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public string? OptionSummary { get; set; }
    public string Unit { get; set; } = "Cái";
    public int WeightGrams { get; set; }
    public string? ImageUrl { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal? CostPrice { get; set; }
    public int WarrantyMonths { get; set; }
    public bool IsActive { get; set; }
    public int? AvailableStock { get; set; }
}
