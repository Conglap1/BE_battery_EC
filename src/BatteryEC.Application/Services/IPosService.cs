using BatteryEC.Application.DTOs.Pos;

namespace BatteryEC.Application.Services;

public interface IPosService
{
    Task<SaleDto> CreateSaleAsync(CreateSaleDto dto, long cashierUserId, CancellationToken cancellationToken = default);
    Task<SaleDto?> GetSaleByCodeAsync(string saleCode, CancellationToken cancellationToken = default);
}
