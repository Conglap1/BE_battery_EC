using BatteryEC.Domain.Common;
using BatteryEC.Domain.Entities.Identity;

namespace BatteryEC.Domain.Entities.Services;

public class RepairTicket : IAggregateRoot
{
    public long RepairTicketId { get; set; }
    public string TicketCode { get; set; } = string.Empty;
    public long? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string DeviceModel { get; set; } = string.Empty;
    public string? SerialOrPinCode { get; set; }
    public string IssueDescription { get; set; } = string.Empty;
    public string? SolutionNote { get; set; }
    public decimal EstimatedCost { get; set; }
    public decimal FinalCost { get; set; }
    public int WarrantyMonths { get; set; }
    public string TicketStatus { get; set; } = "Received";
    public string PaymentStatus { get; set; } = "Unpaid";
    public string? PaymentMethod { get; set; }
    public long CreatedByUserId { get; set; }
    public long? TechnicianUserId { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    // Convenience aliases for DTO compatibility
    public long? TechnicianId { get => TechnicianUserId; set => TechnicianUserId = value; }
    public decimal PaidAmount { get; set; }

    public Customer? Customer { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public User? Technician { get; set; }
    public ICollection<RepairTicketItem> Items { get; set; } = new List<RepairTicketItem>();
}
