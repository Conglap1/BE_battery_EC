using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Reports;
using Microsoft.AspNetCore.Mvc;

namespace BatteryEC.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly IRevenueSummaryQueryService _revenueSummaryService;
    private readonly IInventoryReconciliationQueryService _inventoryReconciliationService;
    private readonly ISupplierDebtQueryService _supplierDebtService;

    public ReportsController(
        IRevenueSummaryQueryService revenueSummaryService,
        IInventoryReconciliationQueryService inventoryReconciliationService,
        ISupplierDebtQueryService supplierDebtService)
    {
        _revenueSummaryService = revenueSummaryService;
        _inventoryReconciliationService = inventoryReconciliationService;
        _supplierDebtService = supplierDebtService;
    }

    [HttpGet("revenue-daily")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RevenueSummaryDto>>>> GetRevenueDaily(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var results = await _revenueSummaryService.QueryDailyAsync(fromDate, toDate, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<RevenueSummaryDto>>.Ok(results));
    }

    [HttpGet("revenue-summary")]
    public async Task<ActionResult<ApiResponse<RevenueSummaryDto>>> GetRevenueSummary(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var result = await _revenueSummaryService.GetAggregateSummaryAsync(fromDate, toDate, cancellationToken);
        return Ok(ApiResponse<RevenueSummaryDto>.Ok(result ?? new RevenueSummaryDto()));
    }

    [HttpGet("inventory-reconciliation")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<InventoryReconciliationDto>>>> GetInventoryReconciliation(
        [FromQuery] bool discrepanciesOnly = false,
        CancellationToken cancellationToken = default)
    {
        var results = await _inventoryReconciliationService.QueryReconciliationAsync(discrepanciesOnly, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<InventoryReconciliationDto>>.Ok(results));
    }

    [HttpGet("supplier-debt")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SupplierDebtSummaryDto>>>> GetSupplierDebt(
        CancellationToken cancellationToken)
    {
        var results = await _supplierDebtService.QuerySupplierDebtAsync(cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<SupplierDebtSummaryDto>>.Ok(results));
    }
}
