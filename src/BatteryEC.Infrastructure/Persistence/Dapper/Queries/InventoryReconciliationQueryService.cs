using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.DTOs.Reports;
using Dapper;

namespace BatteryEC.Infrastructure.Persistence.Dapper.Queries;

public class InventoryReconciliationQueryService : IInventoryReconciliationQueryService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public InventoryReconciliationQueryService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<InventoryReconciliationDto>> QueryReconciliationAsync(bool discrepanciesOnly = false, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        string sql = @"
            SELECT 
                pv.product_variant_id AS ProductVariantId,
                pv.sku AS SKU,
                pv.variant_name AS VariantName,
                COALESCE(inv.quantity_on_hand, 0) AS CurrentStock,
                COALESCE(inv.quantity_reserved, 0) AS ReservedStock,
                r.transaction_quantity AS TotalTransactionHistory,
                r.discrepancy AS Discrepancy,
                r.reconciliation_status AS ReconciliationStatus
            FROM vw_inventory_reconciliation r
            JOIN product_variants pv ON r.product_variant_id = pv.product_variant_id
            LEFT JOIN inventory inv ON pv.product_variant_id = inv.product_variant_id
            WHERE (CAST(@DiscrepanciesOnly AS BOOLEAN) = FALSE OR r.discrepancy <> 0)
            ORDER BY pv.product_variant_id;";

        var result = await connection.QueryAsync<InventoryReconciliationDto>(
            new CommandDefinition(sql, new { DiscrepanciesOnly = discrepanciesOnly }, cancellationToken: cancellationToken));

        return result.AsList();
    }
}
