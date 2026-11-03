using BatteryEC.Domain.Entities.Catalog;
using BatteryEC.Domain.Entities.Identity;
using BatteryEC.Domain.Entities.Inventory;
using BatteryEC.Domain.Entities.Operations;
using BatteryEC.Domain.Entities.Pos;
using BatteryEC.Domain.Entities.Production;
using BatteryEC.Domain.Entities.Purchasing;
using BatteryEC.Domain.Entities.Sales;
using BatteryEC.Domain.Entities.Services;
using Microsoft.EntityFrameworkCore;

namespace BatteryEC.Infrastructure.Persistence;

public class BatteryDbContext : DbContext
{
    public BatteryDbContext(DbContextOptions<BatteryDbContext> options) : base(options)
    {
    }

    // 1. Identity
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Customer> Customers => Set<Customer>();

    // 2. Catalog & BOM
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductAttribute> Attributes => Set<ProductAttribute>();
    public DbSet<AttributeValue> AttributeValues => Set<AttributeValue>();
    public DbSet<ProductVariantAttribute> ProductVariantAttributes => Set<ProductVariantAttribute>();
    public DbSet<ProductVariantBundle> ProductVariantBundles => Set<ProductVariantBundle>();

    // 3. Production
    public DbSet<AssemblyOrder> AssemblyOrders => Set<AssemblyOrder>();
    public DbSet<AssemblyOrderItem> AssemblyOrderItems => Set<AssemblyOrderItem>();

    // 4. Sales & 3PL
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<ShippingMethod> ShippingMethods => Set<ShippingMethod>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Shipment> Shipments => Set<Shipment>();

    // 5. Purchasing & Payables
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseReceipt> PurchaseReceipts => Set<PurchaseReceipt>();
    public DbSet<PurchaseReceiptItem> PurchaseReceiptItems => Set<PurchaseReceiptItem>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();

    // 6. Inventory Ledger
    public DbSet<BatteryEC.Domain.Entities.Inventory.Inventory> Inventory => Set<BatteryEC.Domain.Entities.Inventory.Inventory>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();

    // 7. POS
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();

    // 8. Services & Repair
    public DbSet<RepairTicket> RepairTickets => Set<RepairTicket>();
    public DbSet<RepairTicketItem> RepairTicketItems => Set<RepairTicketItem>();

    // 9. Operations
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ShopExpense> ShopExpenses => Set<ShopExpense>();
    public DbSet<StoreSetting> StoreSettings => Set<StoreSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Ignore<BatteryEC.Domain.Common.DomainEvent>();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BatteryDbContext).Assembly);
    }
}
