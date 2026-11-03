using BatteryEC.Application.DTOs.Reports;

namespace BatteryEC.Application.Common.Interfaces;

public interface IWarrantyLookupQueryService
{
    Task<IReadOnlyList<WarrantyLookupDto>> QueryAsync(string? phoneOrCode = null, CancellationToken cancellationToken = default);
}
