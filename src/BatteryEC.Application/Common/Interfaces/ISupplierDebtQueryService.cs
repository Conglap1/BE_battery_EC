using BatteryEC.Application.DTOs.Reports;

namespace BatteryEC.Application.Common.Interfaces;

public interface ISupplierDebtQueryService
{
    Task<IReadOnlyList<SupplierDebtSummaryDto>> QuerySupplierDebtAsync(CancellationToken cancellationToken = default);
}
