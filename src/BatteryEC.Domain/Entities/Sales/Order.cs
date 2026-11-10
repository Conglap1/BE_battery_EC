using BatteryEC.Domain.Common;
using BatteryEC.Domain.Entities.Identity;

namespace BatteryEC.Domain.Entities.Sales;

public class Order : IAggregateRoot
{
    public long OrderId { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public long UserId { get; set; }
    public int? ShippingMethodId { get; set; }
    public long? VoucherId { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string RecipientPhone { get; set; } = string.Empty;
    public string ShippingAddress { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentMethod { get; set; } = "COD";
    public string PaymentStatus { get; set; } = "Pending";
    public string OrderStatus { get; set; } = "PendingConfirmation";
    public string? CustomerNote { get; set; }
    public string? AdminNote { get; set; }
    public DateTime PlacedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAt { get; set; }
    public long? ConfirmedByUserId { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public User User { get; set; } = null!;
    public ShippingMethod? ShippingMethod { get; set; }
    public Voucher? Voucher { get; set; }
    public User? ConfirmedByUser { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<Shipment> Shipments { get; set; } = new List<Shipment>();
}
