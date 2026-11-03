using BatteryEC.Domain.Entities.Identity;

namespace BatteryEC.Domain.Entities.Purchasing;

public class SupplierPayment
{
    public long SupplierPaymentId { get; set; }
    public long PurchaseReceiptId { get; set; }
    public string PaymentCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "BankTransfer";
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
    public string? ReferenceNumber { get; set; }
    public string? Note { get; set; }
    public long CreatedByUserId { get; set; }

    public PurchaseReceipt PurchaseReceipt { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}
