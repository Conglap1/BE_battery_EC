using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.DTOs.Reports;
using Dapper;

namespace BatteryEC.Infrastructure.Persistence.Dapper.Queries;

public class RevenueSummaryQueryService : IRevenueSummaryQueryService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public RevenueSummaryQueryService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<RevenueSummaryDto>> QueryDailyAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        string sql = @"
            SELECT 
                sale_date AS SaleDate,
                online_order_count AS OnlineOrderCount,
                online_completed_revenue AS OnlineCompletedRevenue,
                online_cancelled_count AS OnlineCancelledCount,
                counter_sale_count AS CounterSaleCount,
                counter_completed_revenue AS CounterCompletedRevenue,
                repair_ticket_count AS RepairTicketCount,
                repair_completed_revenue AS RepairCompletedRevenue,
                daily_expense_amount AS DailyExpenseAmount,
                gross_revenue AS GrossRevenue,
                (gross_revenue - total_cogs - daily_expense_amount) AS RevenueAfterShopExpense
            FROM vw_total_revenue_summary
            WHERE (CAST(@FromDate AS DATE) IS NULL OR sale_date >= CAST(@FromDate AS DATE))
              AND (CAST(@ToDate AS DATE) IS NULL OR sale_date <= CAST(@ToDate AS DATE))
            ORDER BY sale_date DESC;";

        var result = await connection.QueryAsync<RevenueSummaryDto>(
            new CommandDefinition(sql, new { FromDate = fromDate?.Date, ToDate = toDate?.Date }, cancellationToken: cancellationToken));

        return result.AsList();
    }

    public async Task<RevenueSummaryDto?> GetAggregateSummaryAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        string sql = @"
            SELECT 
                COALESCE(SUM(online_order_count), 0) AS OnlineOrderCount,
                COALESCE(SUM(online_completed_revenue), 0) AS OnlineCompletedRevenue,
                COALESCE(SUM(online_cancelled_count), 0) AS OnlineCancelledCount,
                COALESCE(SUM(counter_sale_count), 0) AS CounterSaleCount,
                COALESCE(SUM(counter_completed_revenue), 0) AS CounterCompletedRevenue,
                COALESCE(SUM(repair_ticket_count), 0) AS RepairTicketCount,
                COALESCE(SUM(repair_completed_revenue), 0) AS RepairCompletedRevenue,
                COALESCE(SUM(daily_expense_amount), 0) AS DailyExpenseAmount,
                COALESCE(SUM(gross_revenue), 0) AS GrossRevenue,
                COALESCE(SUM(gross_revenue - total_cogs - daily_expense_amount), 0) AS RevenueAfterShopExpense
            FROM vw_total_revenue_summary
            WHERE (CAST(@FromDate AS DATE) IS NULL OR sale_date >= CAST(@FromDate AS DATE))
              AND (CAST(@ToDate AS DATE) IS NULL OR sale_date <= CAST(@ToDate AS DATE));";

        var result = await connection.QueryFirstOrDefaultAsync<RevenueSummaryDto>(
            new CommandDefinition(sql, new { FromDate = fromDate?.Date, ToDate = toDate?.Date }, cancellationToken: cancellationToken));

        return result ?? new RevenueSummaryDto();
    }
}
