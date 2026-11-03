using BatteryEC.Domain.Common;
using BatteryEC.Domain.Entities.Pos;
using BatteryEC.Domain.Entities.Services;

namespace BatteryEC.Domain.Entities.Identity;

public class Customer : BaseEntity, IAggregateRoot
{
    public long CustomerId { get => Id; set => Id = value; }
    public string Phone { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public long? UserId { get; set; }
    public decimal TotalSpent { get; set; }

    public User? User { get; set; }
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
    public ICollection<RepairTicket> RepairTickets { get; set; } = new List<RepairTicket>();
}
