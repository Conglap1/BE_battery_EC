using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Inventory;
using BatteryEC.Application.Services;
using BatteryEC.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BatteryEC.WebApi.Controllers;

[Authorize(Roles = $"{RoleType.WarehouseStaff},{RoleType.Admin}")]
[ApiController]
[Route("api/[controller]")]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventoryService;
    private readonly ICurrentUserService _currentUserService;

    public InventoryController(IInventoryService inventoryService, ICurrentUserService currentUserService)
    {
        _inventoryService = inventoryService;
        _currentUserService = currentUserService;
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
    public async Task<ActionResult<ApiResponse<bool>>> AdjustStock([FromBody] StockAdjustmentDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            // Security P0.4: Actor identity extracted from authenticated JWT context
            var userId = _currentUserService.UserId ?? throw new UnauthorizedAccessException("User is not authenticated");
            var success = await _inventoryService.AdjustStockAsync(dto, userId, cancellationToken);
            if (!success)
            {
                return BadRequest(ApiResponse<bool>.Fail("Stock adjustment failed. Insufficient stock or invalid adjustment."));
            }

            return Ok(ApiResponse<bool>.Ok(true, "Stock adjusted successfully."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ApiResponse<bool>.Fail(ex.Message));
        }
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
