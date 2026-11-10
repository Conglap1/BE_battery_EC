namespace BatteryEC.Domain.Enums;

public static class ShipmentStatus
{
    public const string Pending = "Pending";
    public const string Preparing = "Preparing";
    public const string PickedUp = "PickedUp";
    public const string Shipped = "Shipped";
    public const string InTransit = "InTransit";
    public const string Delivered = "Delivered";
    public const string Failed = "Failed";
    public const string FailedDelivery = "FailedDelivery";
    public const string Returned = "Returned";
}
