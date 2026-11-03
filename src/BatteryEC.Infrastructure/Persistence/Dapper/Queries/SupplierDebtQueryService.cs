using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.DTOs.Reports;
using Dapper;

namespace BatteryEC.Infrastructure.Persistence.Dapper.Queries;

public class SupplierDebtQueryService : ISupplierDebtQueryService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SupplierDebtQueryService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<SupplierDebtSummaryDto>> QuerySupplierDebtAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        string sql = @"
            SELECT 
                supplier_id AS SupplierId,
                supplier_name AS SupplierName,
                phone AS SupplierPhone,
                total_receipts AS TotalReceipts,
                total_receipt_amount AS TotalPurchasedAmount,
                total_paid_amount AS TotalPaidAmount,
                total_remaining_debt AS TotalOutstandingDebt,
                unpaid_receipt_count AS UnpaidReceiptsCount,
                partially_paid_receipt_count AS PartiallyPaidReceiptsCount,
                paid_receipt_count AS PaidReceiptsCount
            FROM vw_supplier_debt_summary
            ORDER BY total_remaining_debt DESC;";

        var result = await connection.QueryAsync<SupplierDebtSummaryDto>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));

        return result.AsList();
    }
}
