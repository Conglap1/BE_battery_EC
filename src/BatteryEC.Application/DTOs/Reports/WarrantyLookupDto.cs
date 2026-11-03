namespace BatteryEC.Application.DTOs.Reports;

public class WarrantyLookupDto
{
    public string SaleChannel { get; set; } = string.Empty;
    public string InvoiceCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public int WarrantyMonths { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public DateTime? WarrantyExpireDate { get; set; }
    public string WarrantyStatus { get; set; } = string.Empty;
}
