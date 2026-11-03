using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.DTOs.Reports;
using Dapper;

namespace BatteryEC.Infrastructure.Persistence.Dapper.Queries;

public class WarrantyLookupQueryService : IWarrantyLookupQueryService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public WarrantyLookupQueryService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<WarrantyLookupDto>> QueryAsync(string? phoneOrCode = null, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        string sql = @"
            SELECT 
                sale_channel AS SaleChannel,
                invoice_code AS InvoiceCode,
                customer_name AS CustomerName,
                customer_phone AS CustomerPhone,
                product_name AS ProductName,
                variant_name AS VariantName,
                sku AS SKU,
                warranty_months AS WarrantyMonths,
                purchase_date AS PurchaseDate,
                warranty_expire_date AS WarrantyExpireDate,
                warranty_status AS WarrantyStatus
            FROM vw_warranty_lookup
            WHERE (CAST(@Query AS TEXT) IS NULL OR customer_phone ILIKE @Query OR invoice_code ILIKE @Query OR sku ILIKE @Query)
            ORDER BY purchase_date DESC NULLS LAST;";

        var queryParam = string.IsNullOrWhiteSpace(phoneOrCode) ? null : $"%{phoneOrCode.Trim()}%";

        var result = await connection.QueryAsync<WarrantyLookupDto>(
            new CommandDefinition(sql, new { Query = queryParam }, cancellationToken: cancellationToken));

        return result.AsList();
    }
}
