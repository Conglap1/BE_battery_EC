using BatteryEC.Domain.Entities.Pos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryEC.Infrastructure.Persistence.Configurations;

public class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("sales");
        builder.HasKey(s => s.SaleId);
        builder.Property(s => s.SaleId).UseIdentityAlwaysColumn();

        builder.Property(s => s.SaleCode).HasMaxLength(40).IsRequired();
        builder.Property(s => s.CustomerName).HasMaxLength(100);
        builder.Property(s => s.CustomerPhone).HasMaxLength(20);
        builder.Property(s => s.SubtotalAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.DiscountAmount).HasPrecision(18, 2).HasDefaultValue(0m);
        builder.Property(s => s.TotalAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.PaymentMethod).HasMaxLength(30).HasDefaultValue("Cash");
        builder.Property(s => s.SaleStatus).HasMaxLength(20).HasDefaultValue("Completed");
        builder.Property(s => s.Note).HasMaxLength(500);
        builder.Property(s => s.SoldAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Ignore(s => s.CreatedByUserId);
        builder.Ignore(s => s.Subtotal);

        builder.HasIndex(s => s.SaleCode).IsUnique();
        builder.HasIndex(s => s.SoldByUserId);
        builder.HasIndex(s => s.CustomerId);
        builder.HasIndex(s => s.SoldAt);

        builder.HasOne(s => s.SoldByUser)
            .WithMany(u => u.Sales)
            .HasForeignKey(s => s.SoldByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Customer)
            .WithMany(c => c.Sales)
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class SaleItemConfiguration : IEntityTypeConfiguration<SaleItem>
{
    public void Configure(EntityTypeBuilder<SaleItem> builder)
    {
        builder.ToTable("sale_items");
        builder.HasKey(si => si.SaleItemId);
        builder.Property(si => si.SaleItemId).UseIdentityAlwaysColumn();

        builder.Property(si => si.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(si => si.VariantName).HasMaxLength(150).IsRequired();
        builder.Property(si => si.SKU).HasMaxLength(50).IsRequired();
        builder.Property(si => si.UnitPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(si => si.CostPrice).HasPrecision(18, 2).HasDefaultValue(0m);
        builder.Property(si => si.Quantity).IsRequired();
        builder.Property(si => si.WarrantyMonths).HasDefaultValue(0);
        builder.Property(si => si.LineTotal).HasPrecision(18, 2).ValueGeneratedOnAddOrUpdate();

        builder.HasIndex(si => si.SaleId);
        builder.HasIndex(si => si.ProductVariantId);

        builder.HasOne(si => si.Sale)
            .WithMany(s => s.Items)
            .HasForeignKey(si => si.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(si => si.ProductVariant)
            .WithMany(pv => pv.SaleItems)
            .HasForeignKey(si => si.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
