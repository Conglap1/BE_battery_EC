using BatteryEC.Domain.Common;
using BatteryEC.Domain.Entities.Identity;

namespace BatteryEC.Domain.Entities.Purchasing;

public class PurchaseReceipt : IAggregateRoot
{
    public long PurchaseReceiptId { get; set; }
    public string ReceiptCode { get; set; } = string.Empty;
    public long SupplierId { get; set; }
    public long CreatedByUserId { get; set; }
    public string ReceiptStatus { get; set; } = "Draft";
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public string PaymentStatus { get; set; } = "Unpaid";
    public string? SupplierInvoiceNo { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReceivedAt { get; set; }

    public Supplier Supplier { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public ICollection<PurchaseReceiptItem> Items { get; set; } = new List<PurchaseReceiptItem>();
    public ICollection<SupplierPayment> Payments { get; set; } = new List<SupplierPayment>();
}
