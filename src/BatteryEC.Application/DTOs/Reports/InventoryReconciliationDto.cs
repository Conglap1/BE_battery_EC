namespace BatteryEC.Application.DTOs.Reports;

public class InventoryReconciliationDto
{
    public long ProductVariantId { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public int CurrentStock { get; set; }
    public int ReservedStock { get; set; }
    public int TotalTransactionHistory { get; set; }
    public int Discrepancy { get; set; }
    public string ReconciliationStatus { get; set; } = string.Empty;
}
