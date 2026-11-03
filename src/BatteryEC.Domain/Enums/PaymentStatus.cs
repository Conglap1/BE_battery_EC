namespace BatteryEC.Domain.Enums;

public static class PaymentStatus
{
    // Orders / PurchaseReceipts statuses
    public const string Unpaid = "Unpaid";
    public const string PartiallyPaid = "PartiallyPaid";
    public const string Processing = "Processing";
    public const string Paid = "Paid";
    public const string Cancelled = "Cancelled";

    // Payments transaction statuses
    public const string Pending = "Pending";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Refunded = "Refunded";
}
