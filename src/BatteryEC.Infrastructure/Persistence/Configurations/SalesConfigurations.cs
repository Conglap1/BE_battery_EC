using BatteryEC.Domain.Entities.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryEC.Infrastructure.Persistence.Configurations;

public class VoucherConfiguration : IEntityTypeConfiguration<Voucher>
{
    public void Configure(EntityTypeBuilder<Voucher> builder)
    {
        builder.ToTable("vouchers");
        builder.HasKey(v => v.VoucherId);
        builder.Property(v => v.VoucherId).UseIdentityAlwaysColumn();
        builder.Property(v => v.Code).HasMaxLength(50).IsRequired();
        builder.Property(v => v.Description).HasMaxLength(500);
        builder.Property(v => v.DiscountType).HasMaxLength(20).IsRequired();
        builder.Property(v => v.DiscountValue).HasPrecision(18, 2).IsRequired();
        builder.Property(v => v.MinimumOrderAmount).HasPrecision(18, 2).HasDefaultValue(0m);
        builder.Property(v => v.MaximumDiscountAmount).HasPrecision(18, 2);
        builder.Property(v => v.IsActive).HasDefaultValue(true);

        builder.HasIndex(v => v.Code).IsUnique();
        builder.HasIndex(v => new { v.Code, v.StartsAt, v.EndsAt, v.IsActive });
    }
}

public class ShippingMethodConfiguration : IEntityTypeConfiguration<ShippingMethod>
{
    public void Configure(EntityTypeBuilder<ShippingMethod> builder)
    {
        builder.ToTable("shipping_methods");
        builder.HasKey(sm => sm.ShippingMethodId);
        builder.Property(sm => sm.ShippingMethodId).UseIdentityAlwaysColumn();
        builder.Property(sm => sm.Name).HasMaxLength(100).IsRequired();
        builder.Property(sm => sm.Description).HasMaxLength(300);
        builder.Property(sm => sm.BaseFee).HasPrecision(18, 2).HasDefaultValue(0m);
        builder.Property(sm => sm.IsActive).HasDefaultValue(true);
    }
}

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.OrderId);
        builder.Property(o => o.OrderId).UseIdentityAlwaysColumn();
        builder.Property(o => o.OrderCode).HasMaxLength(40).IsRequired();
        builder.Property(o => o.RecipientName).HasMaxLength(150).IsRequired();
        builder.Property(o => o.RecipientPhone).HasMaxLength(20).IsRequired();
        builder.Property(o => o.ShippingAddress).HasMaxLength(600).IsRequired();
        builder.Property(o => o.Subtotal).HasPrecision(18, 2).IsRequired();
        builder.Property(o => o.DiscountAmount).HasPrecision(18, 2).HasDefaultValue(0m);
        builder.Property(o => o.ShippingFee).HasPrecision(18, 2).HasDefaultValue(0m);
        builder.Property(o => o.TotalAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(o => o.PaymentMethod).HasMaxLength(30).HasDefaultValue("COD");
        builder.Property(o => o.PaymentStatus).HasMaxLength(30).HasDefaultValue("Unpaid");
        builder.Property(o => o.OrderStatus).HasMaxLength(30).HasDefaultValue("PendingConfirmation");
        builder.Property(o => o.CustomerNote).HasMaxLength(1000);
        builder.Property(o => o.AdminNote).HasMaxLength(1000);

        builder.HasIndex(o => o.OrderCode).IsUnique();
        builder.HasIndex(o => new { o.UserId, o.PlacedAt });
        builder.HasIndex(o => new { o.OrderStatus, o.PlacedAt });

        builder.HasOne(o => o.User)
            .WithMany(u => u.Orders)
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.ShippingMethod)
            .WithMany(sm => sm.Orders)
            .HasForeignKey(o => o.ShippingMethodId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(o => o.Voucher)
            .WithMany(v => v.Orders)
            .HasForeignKey(o => o.VoucherId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(o => o.ConfirmedByUser)
            .WithMany(u => u.ConfirmedOrders)
            .HasForeignKey(o => o.ConfirmedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");
        builder.HasKey(oi => oi.OrderItemId);
        builder.Property(oi => oi.OrderItemId).UseIdentityAlwaysColumn();
        builder.Property(oi => oi.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(oi => oi.VariantName).HasMaxLength(150).IsRequired();
        builder.Property(oi => oi.SKU).HasMaxLength(64).IsRequired();
        builder.Property(oi => oi.UnitPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(oi => oi.CostPrice).HasPrecision(18, 2);
        builder.Property(oi => oi.WarrantyMonths).HasDefaultValue(0);
        builder.Property(oi => oi.LineTotal).HasPrecision(18, 2).IsRequired();

        builder.HasOne(oi => oi.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(oi => oi.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(oi => oi.ProductVariant)
            .WithMany(pv => pv.OrderItems)
            .HasForeignKey(oi => oi.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(oi => oi.OrderId);
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(p => p.PaymentId);
        builder.Property(p => p.PaymentId).UseIdentityAlwaysColumn();
        builder.Property(p => p.PaymentMethod).HasMaxLength(30).IsRequired();
        builder.Property(p => p.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(p => p.PaymentStatus).HasMaxLength(30).HasDefaultValue("Pending");
        builder.Property(p => p.Provider).HasMaxLength(100);
        builder.Property(p => p.TransactionCode).HasMaxLength(200);

        builder.HasOne(p => p.Order)
            .WithMany(o => o.Payments)
            .HasForeignKey(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.OrderId);
    }
}

public class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("shipments");
        builder.HasKey(s => s.ShipmentId);
        builder.Property(s => s.ShipmentId).UseIdentityAlwaysColumn();
        builder.Property(s => s.CarrierName).HasMaxLength(100);
        builder.Property(s => s.TrackingNumber).HasMaxLength(100);
        builder.Property(s => s.ShipmentStatus).HasMaxLength(30).HasDefaultValue("Preparing");
        builder.Property(s => s.ShippingFee).HasPrecision(18, 2).HasDefaultValue(0m);
        builder.Property(s => s.Note).HasMaxLength(500);

        builder.HasOne(s => s.Order)
            .WithMany(o => o.Shipments)
            .HasForeignKey(s => s.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.ShippingMethod)
            .WithMany(sm => sm.Shipments)
            .HasForeignKey(s => s.ShippingMethodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => s.OrderId);
    }
}
