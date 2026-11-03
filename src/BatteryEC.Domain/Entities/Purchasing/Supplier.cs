using BatteryEC.Domain.Common;

namespace BatteryEC.Domain.Entities.Purchasing;

public class Supplier : IAggregateRoot
{
    public long SupplierId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? TaxCode { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PurchaseReceipt> Receipts { get; set; } = new List<PurchaseReceipt>();
}
