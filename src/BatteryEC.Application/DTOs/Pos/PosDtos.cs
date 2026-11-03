namespace BatteryEC.Application.DTOs.Pos;

public class CreateSaleDto
{
    public long? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public decimal DiscountAmount { get; set; }
    public string PaymentMethod { get; set; } = "Cash";
    public string? Note { get; set; }
    public List<CreateSaleItemDto> Items { get; set; } = new();
}

public class CreateSaleItemDto
{
    public long ProductVariantId { get; set; }
    public int Quantity { get; set; }
}

public class SaleDto
{
    public long SaleId { get; set; }
    public string SaleCode { get; set; } = string.Empty;
    public long CreatedByUserId { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string SaleStatus { get; set; } = string.Empty;
    public DateTime SoldAt { get; set; }
    public List<SaleItemDto> Items { get; set; } = new();
}

public class SaleItemDto
{
    public long SaleItemId { get; set; }
    public long ProductVariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int WarrantyMonths { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}
