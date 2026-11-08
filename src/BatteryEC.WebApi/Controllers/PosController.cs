using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Pos;
using BatteryEC.Application.Services;
using BatteryEC.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BatteryEC.WebApi.Controllers;

[Authorize(Roles = $"{RoleType.SalesStaff},{RoleType.Admin}")]
[ApiController]
[Route("api/[controller]")]
public class PosController : ControllerBase
{
    private readonly IPosService _posService;
    private readonly ICurrentUserService _currentUserService;

    public PosController(IPosService posService, ICurrentUserService currentUserService)
    {
        _posService = posService;
        _currentUserService = currentUserService;
    }

    [HttpPost("sales")]
    public async Task<ActionResult<ApiResponse<SaleDto>>> CreateSale([FromBody] CreateSaleDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            // Security P0.4: Cashier identity extracted from authenticated JWT context
            var cashierUserId = _currentUserService.UserId ?? throw new UnauthorizedAccessException("User is not authenticated");
            var sale = await _posService.CreateSaleAsync(dto, cashierUserId, cancellationToken);
            return CreatedAtAction(nameof(GetSaleByCode), new { code = sale.SaleCode }, ApiResponse<SaleDto>.Ok(sale, "POS sale completed successfully."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ApiResponse<SaleDto>.Fail(ex.Message));
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
