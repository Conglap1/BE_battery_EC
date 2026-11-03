using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BatteryEC.WebApi.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/[controller]")]
public class WarrantyController : ControllerBase
{
    private readonly IWarrantyLookupQueryService _warrantyLookupService;

    public WarrantyController(IWarrantyLookupQueryService warrantyLookupService)
    {
        _warrantyLookupService = warrantyLookupService;
    }

    [AllowAnonymous]
    [HttpGet("lookup")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WarrantyLookupDto>>>> LookupWarranty(
        [FromQuery] string? query,
        [FromQuery] string? serialNumber,
        CancellationToken cancellationToken)
    {
        var searchTerm = !string.IsNullOrWhiteSpace(query) ? query : serialNumber;
        var results = await _warrantyLookupService.QueryAsync(searchTerm, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<WarrantyLookupDto>>.Ok(results));
    }
}
