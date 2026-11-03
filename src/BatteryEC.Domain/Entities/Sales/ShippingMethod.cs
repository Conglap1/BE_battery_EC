namespace BatteryEC.Domain.Entities.Sales;

public class ShippingMethod
{
    public int ShippingMethodId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal BaseFee { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<Shipment> Shipments { get; set; } = new List<Shipment>();
}
