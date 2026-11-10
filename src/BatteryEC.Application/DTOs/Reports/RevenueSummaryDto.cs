namespace BatteryEC.Application.DTOs.Reports;

public class RevenueSummaryDto
{
    public DateTime SaleDate { get; set; }

    // Transaction counts
    public long OnlineOrderCount { get; set; }
    public long OnlineCancelledCount { get; set; }
    public long CounterSaleCount { get; set; }
    public long RepairTicketCount { get; set; }

    // Revenue streams
    public decimal OnlineCompletedRevenue { get; set; }
    public decimal CounterCompletedRevenue { get; set; }
    public decimal RepairCompletedRevenue { get; set; }
    public decimal GrossRevenue { get; set; }

    // Cost of Goods Sold (COGS)
    public decimal OnlineCompletedCogs { get; set; }
    public decimal CounterCompletedCogs { get; set; }
    public decimal RepairCompletedCogs { get; set; }
    public decimal TotalCogs { get; set; }

    // Gross Profit & Margin
    public decimal GrossProfit { get; set; }
    public decimal GrossMarginPercent => GrossRevenue > 0 ? Math.Round((GrossProfit / GrossRevenue) * 100, 2) : 0;

    // Operating Expenses & Net Profit
    public decimal DailyExpenseAmount { get; set; }
    public decimal NetProfit { get; set; }
    public decimal NetMarginPercent => GrossRevenue > 0 ? Math.Round((NetProfit / GrossRevenue) * 100, 2) : 0;

    // Backward-compatibility alias for previous API consumers
    public decimal RevenueAfterShopExpense => NetProfit;
}
