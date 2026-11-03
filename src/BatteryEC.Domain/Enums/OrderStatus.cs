namespace BatteryEC.Domain.Enums;

public static class OrderStatus
{
    public const string PendingConfirmation = "PendingConfirmation";
    public const string Confirmed = "Confirmed";
    public const string Processing = "Processing";
    public const string Shipped = "Shipped";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}
