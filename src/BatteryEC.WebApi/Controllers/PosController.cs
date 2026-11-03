using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Pos;
using BatteryEC.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace BatteryEC.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PosController : ControllerBase
{
    private readonly IPosService _posService;

    public PosController(IPosService posService)
    {
        _posService = posService;
    }

    [HttpPost("sales")]
    public async Task<ActionResult<ApiResponse<SaleDto>>> CreateSale([FromBody] CreateSaleDto dto, [FromQuery] long cashierUserId = 1, CancellationToken cancellationToken = default)
    {
        try
        {
            var sale = await _posService.CreateSaleAsync(dto, cashierUserId, cancellationToken);
            return CreatedAtAction(nameof(GetSaleByCode), new { code = sale.SaleCode }, ApiResponse<SaleDto>.Ok(sale, "POS sale completed successfully."));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<SaleDto>.Fail(ex.Message));
        }
    }

    [HttpGet("sales/{code}")]
    public async Task<ActionResult<ApiResponse<SaleDto>>> GetSaleByCode(string code, CancellationToken cancellationToken)
    {
        var sale = await _posService.GetSaleByCodeAsync(code, cancellationToken);
        if (sale == null)
        {
            return NotFound(ApiResponse<SaleDto>.Fail($"Sale '{code}' not found."));
        }

        return Ok(ApiResponse<SaleDto>.Ok(sale));
    }
}
