using BatteryEC.Domain.Common;
using BatteryEC.Domain.Entities.Inventory;
using BatteryEC.Domain.Entities.Operations;
using BatteryEC.Domain.Entities.Pos;
using BatteryEC.Domain.Entities.Production;
using BatteryEC.Domain.Entities.Purchasing;
using BatteryEC.Domain.Entities.Sales;
using BatteryEC.Domain.Entities.Services;

namespace BatteryEC.Domain.Entities.Identity;

public class User : BaseEntity, IAggregateRoot
{
    public long UserId { get => Id; set => Id = value; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiryTime { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? EmailVerifiedAt { get; set; }
    public DateTime? PhoneVerifiedAt { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<Address> Addresses { get; set; } = new List<Address>();
    public Customer? Customer { get; set; }
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<Order> ConfirmedOrders { get; set; } = new List<Order>();
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
    public ICollection<AssemblyOrder> AssemblyOrders { get; set; } = new List<AssemblyOrder>();
    public ICollection<PurchaseReceipt> PurchaseReceipts { get; set; } = new List<PurchaseReceipt>();
    public ICollection<SupplierPayment> SupplierPayments { get; set; } = new List<SupplierPayment>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<RepairTicket> CreatedRepairTickets { get; set; } = new List<RepairTicket>();
    public ICollection<RepairTicket> TechnicianRepairTickets { get; set; } = new List<RepairTicket>();
    public ICollection<ShopExpense> ShopExpenses { get; set; } = new List<ShopExpense>();
    public ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();
}
