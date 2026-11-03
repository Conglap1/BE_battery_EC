using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Reports;
using Microsoft.AspNetCore.Mvc;

namespace BatteryEC.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WarrantyController : ControllerBase
{
    private readonly IWarrantyLookupQueryService _warrantyLookupService;

    public WarrantyController(IWarrantyLookupQueryService warrantyLookupService)
    {
        _warrantyLookupService = warrantyLookupService;
    }

    [HttpGet("lookup")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WarrantyLookupDto>>>> LookupWarranty(
        [FromQuery] string? query,
        CancellationToken cancellationToken)
    {
        var results = await _warrantyLookupService.QueryAsync(query, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<WarrantyLookupDto>>.Ok(results));
    }
}
