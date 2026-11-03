using BatteryEC.Application.DTOs.Reports;

namespace BatteryEC.Application.Common.Interfaces;

public interface IInventoryReconciliationQueryService
{
    Task<IReadOnlyList<InventoryReconciliationDto>> QueryReconciliationAsync(bool discrepanciesOnly = false, CancellationToken cancellationToken = default);
}
