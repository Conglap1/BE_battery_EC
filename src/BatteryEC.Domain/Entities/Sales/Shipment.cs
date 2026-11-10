namespace BatteryEC.Domain.Entities.Sales;

public class Shipment
{
    public long ShipmentId { get; set; }
    public long OrderId { get; set; }
    public int ShippingMethodId { get; set; }
    public string? CarrierName { get; set; }
    public string? TrackingNumber { get; set; }
    public string ShipmentStatus { get; set; } = "Pending";
    public decimal ShippingFee { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Order Order { get; set; } = null!;
    public ShippingMethod ShippingMethod { get; set; } = null!;
}
