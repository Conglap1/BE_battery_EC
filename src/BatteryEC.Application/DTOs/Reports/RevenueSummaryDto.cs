namespace BatteryEC.Application.DTOs.Reports;

public class RevenueSummaryDto
{
    public DateTime SaleDate { get; set; }
    public long OnlineOrderCount { get; set; }
    public decimal OnlineCompletedRevenue { get; set; }
    public long OnlineCancelledCount { get; set; }
    public long CounterSaleCount { get; set; }
    public decimal CounterCompletedRevenue { get; set; }
    public long RepairTicketCount { get; set; }
    public decimal RepairCompletedRevenue { get; set; }
    public decimal DailyExpenseAmount { get; set; }
    public decimal GrossRevenue { get; set; }
    public decimal RevenueAfterShopExpense { get; set; }
}
