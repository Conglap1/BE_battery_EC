using BatteryEC.Application.DTOs.Orders;

namespace BatteryEC.Application.Services;

public interface IOrderService
{
    Task<OrderDto> CreateOrderAsync(CreateOrderDto dto, CancellationToken cancellationToken = default);
    Task<OrderDto?> GetOrderByCodeAsync(string orderCode, CancellationToken cancellationToken = default);
    Task<bool> ConfirmOrderAsync(long orderId, long confirmedByUserId, CancellationToken cancellationToken = default);
    Task<bool> CancelOrderAsync(long orderId, CancellationToken cancellationToken = default);
}
