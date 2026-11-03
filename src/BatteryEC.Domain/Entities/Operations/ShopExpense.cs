using BatteryEC.Domain.Entities.Identity;

namespace BatteryEC.Domain.Entities.Operations;

public class ShopExpense
{
    public long ExpenseId { get; set; }
    public string ExpenseCode { get; set; } = string.Empty;
    public string ExpenseCategory { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public long PaidByUserId { get; set; }
    public string? Note { get; set; }
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;

    // Convenience aliases
    public string Category { get => ExpenseCategory; set => ExpenseCategory = value; }
    public long CreatedByUserId { get => PaidByUserId; set => PaidByUserId = value; }
    public string PaymentMethod { get; set; } = "Cash";

    public User PaidByUser { get; set; } = null!;
}
