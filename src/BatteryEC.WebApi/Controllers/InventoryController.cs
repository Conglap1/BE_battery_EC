using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Inventory;
using BatteryEC.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace BatteryEC.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventoryService;

    public InventoryController(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    [HttpGet("{variantId:long}")]
    public async Task<ActionResult<ApiResponse<InventoryDto>>> GetStock(long variantId, CancellationToken cancellationToken)
    {
        var inventory = await _inventoryService.GetStockAsync(variantId, cancellationToken);
        if (inventory == null)
        {
            return NotFound(ApiResponse<InventoryDto>.Fail($"Inventory for variant #{variantId} not found."));
        }

        return Ok(ApiResponse<InventoryDto>.Ok(inventory));
    }

    [HttpPost("adjust")]
    public async Task<ActionResult<ApiResponse<bool>>> AdjustStock([FromBody] StockAdjustmentDto dto, [FromQuery] long userId = 1, CancellationToken cancellationToken = default)
    {
        var success = await _inventoryService.AdjustStockAsync(dto, userId, cancellationToken);
        if (!success)
        {
            return BadRequest(ApiResponse<bool>.Fail("Stock adjustment failed. Insufficient stock or invalid adjustment."));
        }

        return Ok(ApiResponse<bool>.Ok(true, "Stock adjusted successfully."));
    }

    [HttpPost("reserve")]
    public async Task<ActionResult<ApiResponse<bool>>> ReserveStock([FromQuery] long variantId, [FromQuery] int quantity, CancellationToken cancellationToken)
    {
        var success = await _inventoryService.ReserveStockAsync(variantId, quantity, cancellationToken);
        if (!success)
        {
            return BadRequest(ApiResponse<bool>.Fail($"Unable to reserve {quantity} units for variant #{variantId}. Insufficient available stock."));
        }

        return Ok(ApiResponse<bool>.Ok(true, "Stock reserved successfully."));
    }

    [HttpPost("release")]
    public async Task<ActionResult<ApiResponse<bool>>> ReleaseStock([FromQuery] long variantId, [FromQuery] int quantity, CancellationToken cancellationToken)
    {
        var success = await _inventoryService.ReleaseStockAsync(variantId, quantity, cancellationToken);
        if (!success)
        {
            return BadRequest(ApiResponse<bool>.Fail($"Unable to release reserved stock for variant #{variantId}."));
        }

        return Ok(ApiResponse<bool>.Ok(true, "Reserved stock released successfully."));
    }
}
