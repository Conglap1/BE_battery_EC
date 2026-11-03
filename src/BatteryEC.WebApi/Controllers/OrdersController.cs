using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Orders;
using BatteryEC.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace BatteryEC.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<OrderDto>>> CreateOrder([FromBody] CreateOrderDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var order = await _orderService.CreateOrderAsync(dto, cancellationToken);
            return CreatedAtAction(nameof(GetOrderByCode), new { code = order.OrderCode }, ApiResponse<OrderDto>.Ok(order, "Order placed successfully."));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<OrderDto>.Fail(ex.Message));
        }
    }

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

    [HttpPost("{id:long}/confirm")]
    public async Task<ActionResult<ApiResponse<bool>>> ConfirmOrder(long id, [FromQuery] long userId = 1, CancellationToken cancellationToken = default)
    {
        var success = await _orderService.ConfirmOrderAsync(id, userId, cancellationToken);
        if (!success)
        {
            return BadRequest(ApiResponse<bool>.Fail($"Unable to confirm order #{id}."));
        }

        return Ok(ApiResponse<bool>.Ok(true, "Order confirmed successfully."));
    }

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
