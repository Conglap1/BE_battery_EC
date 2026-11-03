using BatteryEC.Domain.Common;
using BatteryEC.Domain.Entities.Identity;

namespace BatteryEC.Domain.Entities.Pos;

public class Sale : IAggregateRoot
{
    public long SaleId { get; set; }
    public string SaleCode { get; set; } = string.Empty;
    public long SoldByUserId { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentMethod { get; set; } = "Cash";
    public string SaleStatus { get; set; } = "Completed";
    public string? Note { get; set; }
    public DateTime SoldAt { get; set; } = DateTime.UtcNow;

    // Convenience aliases for DTO compatibility
    public long CreatedByUserId { get => SoldByUserId; set => SoldByUserId = value; }
    public decimal Subtotal { get => SubtotalAmount; set => SubtotalAmount = value; }

    public User SoldByUser { get; set; } = null!;
    public Customer? Customer { get; set; }
    public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
}
