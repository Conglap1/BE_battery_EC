namespace BatteryEC.Domain.Enums;

public static class RepairTicketStatus
{
    public const string Received = "Received";
    public const string Diagnosing = "Diagnosing";
    public const string WaitingCustomer = "WaitingCustomer";
    public const string Repairing = "Repairing";
    public const string ReadyForPickup = "ReadyForPickup";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}
