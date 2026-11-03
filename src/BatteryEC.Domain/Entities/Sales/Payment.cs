namespace BatteryEC.Domain.Entities.Sales;

public class Payment
{
    public long PaymentId { get; set; }
    public long OrderId { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentStatus { get; set; } = "Pending";
    public string? Provider { get; set; }
    public string? TransactionCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }

    public Order Order { get; set; } = null!;
}
