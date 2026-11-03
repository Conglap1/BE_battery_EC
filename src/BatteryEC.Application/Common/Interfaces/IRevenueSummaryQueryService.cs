using BatteryEC.Application.DTOs.Reports;

namespace BatteryEC.Application.Common.Interfaces;

public interface IRevenueSummaryQueryService
{
    Task<IReadOnlyList<RevenueSummaryDto>> QueryDailyAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);
    Task<RevenueSummaryDto?> GetAggregateSummaryAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);
}
