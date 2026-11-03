namespace BatteryEC.Application.DTOs.Reports;

public class SupplierDebtSummaryDto
{
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierPhone { get; set; }
    public int TotalReceipts { get; set; }
    public decimal TotalPurchasedAmount { get; set; }
    public decimal TotalPaidAmount { get; set; }
    public decimal TotalOutstandingDebt { get; set; }
    public int UnpaidReceiptsCount { get; set; }
    public int PartiallyPaidReceiptsCount { get; set; }
    public int PaidReceiptsCount { get; set; }
}
