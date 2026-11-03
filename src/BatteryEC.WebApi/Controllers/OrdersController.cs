using BatteryEC.Application.Common.Interfaces;
using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Orders;
using BatteryEC.Application.Services;
using BatteryEC.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BatteryEC.WebApi.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ICurrentUserService _currentUserService;

    public OrdersController(IOrderService orderService, ICurrentUserService currentUserService)
    {
        _orderService = orderService;
        _currentUserService = currentUserService;
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<ApiResponse<OrderDto>>> CreateOrder([FromBody] CreateOrderDto dto, CancellationToken cancellationToken)
    {
        try
        {
            if (dto.UserId <= 0)
            {
                dto.UserId = _currentUserService.UserId ?? 1;
            }

            var order = await _orderService.CreateOrderAsync(dto, cancellationToken);
            return CreatedAtAction(nameof(GetOrderByCode), new { code = order.OrderCode }, ApiResponse<OrderDto>.Ok(order, "Order placed successfully."));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<OrderDto>.Fail(ex.Message));
        }
    }

    [Authorize]
    [HttpGet("{code}")]
    public async Task<ActionResult<ApiResponse<OrderDto>>> GetOrderByCode(string code, CancellationToken cancellationToken)
    {
        var order = await _orderService.GetOrderByCodeAsync(code, cancellationToken);
        if (order == null)
        {
            return NotFound(ApiResponse<OrderDto>.Fail($"Order '{code}' not found."));
        }

        return Ok(ApiResponse<OrderDto>.Ok(order));
    }

    [Authorize(Roles = $"{RoleType.StoreManager},{RoleType.Admin}")]
    [HttpPost("{id:long}/confirm")]
    public async Task<ActionResult<ApiResponse<bool>>> ConfirmOrder(long id, [FromQuery] long userId = 1, CancellationToken cancellationToken = default)
    {
        var effectiveUserId = userId > 0 ? userId : (_currentUserService.UserId ?? 1);
        var success = await _orderService.ConfirmOrderAsync(id, effectiveUserId, cancellationToken);
        if (!success)
        {
            return BadRequest(ApiResponse<bool>.Fail($"Unable to confirm order #{id}."));
        }

        return Ok(ApiResponse<bool>.Ok(true, "Order confirmed successfully."));
    }

    [Authorize(Roles = $"{RoleType.StoreManager},{RoleType.Admin}")]
    [HttpPost("{id:long}/ship")]
    public async Task<ActionResult<ApiResponse<bool>>> ShipOrder(
        long id,
        [FromQuery] string carrier = "GHTK",
        [FromQuery] string trackingCode = "",
        [FromQuery] long userId = 1,
        CancellationToken cancellationToken = default)
    {
        var effectiveUserId = userId > 0 ? userId : (_currentUserService.UserId ?? 1);
        var code = string.IsNullOrWhiteSpace(trackingCode) ? $"TRACK-{DateTime.UtcNow.Ticks}" : trackingCode;
        var success = await _orderService.ShipOrderAsync(id, carrier, code, effectiveUserId, cancellationToken);
        if (!success)
        {
            return BadRequest(ApiResponse<bool>.Fail($"Unable to mark order #{id} as shipped. Ensure status is Confirmed or Processing."));
        }

        return Ok(ApiResponse<bool>.Ok(true, "Order handed over to carrier successfully."));
    }

    [Authorize(Roles = $"{RoleType.StoreManager},{RoleType.Admin}")]
    [HttpPost("{id:long}/complete")]
    public async Task<ActionResult<ApiResponse<bool>>> CompleteOrder(long id, CancellationToken cancellationToken)
    {
        var success = await _orderService.CompleteOrderAsync(id, cancellationToken);
        if (!success)
        {
            return BadRequest(ApiResponse<bool>.Fail($"Unable to complete order #{id}. Ensure status is Shipped."));
        }

        return Ok(ApiResponse<bool>.Ok(true, "Order completed, real stock deducted, and warranty activated."));
    }

    [Authorize(Roles = $"{RoleType.StoreManager},{RoleType.Admin}")]
    [HttpPost("{id:long}/cancel")]
    public async Task<ActionResult<ApiResponse<bool>>> CancelOrder(long id, CancellationToken cancellationToken)
    {
        var success = await _orderService.CancelOrderAsync(id, cancellationToken);
        if (!success)
        {
            return BadRequest(ApiResponse<bool>.Fail($"Unable to cancel order #{id}."));
        }

        return Ok(ApiResponse<bool>.Ok(true, "Order cancelled and stock released successfully."));
    }
}
