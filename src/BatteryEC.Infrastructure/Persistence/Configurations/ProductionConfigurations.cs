using BatteryEC.Domain.Entities.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryEC.Infrastructure.Persistence.Configurations;

public class ProductVariantBundleConfiguration : IEntityTypeConfiguration<ProductVariantBundle>
{
    public void Configure(EntityTypeBuilder<ProductVariantBundle> builder)
    {
        builder.ToTable("product_variant_bundles");
        builder.HasKey(pvb => new { pvb.BundleVariantId, pvb.ComponentVariantId });

        builder.Property(pvb => pvb.Quantity).IsRequired();

        builder.HasOne(pvb => pvb.BundleVariant)
            .WithMany(pv => pv.BundlesAsParent)
            .HasForeignKey(pvb => pvb.BundleVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(pvb => pvb.ComponentVariant)
            .WithMany(pv => pv.BundlesAsComponent)
            .HasForeignKey(pvb => pvb.ComponentVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(pvb => pvb.ComponentVariantId);
    }
}

public class AssemblyOrderConfiguration : IEntityTypeConfiguration<AssemblyOrder>
{
    public void Configure(EntityTypeBuilder<AssemblyOrder> builder)
    {
        builder.ToTable("assembly_orders");
        builder.HasKey(ao => ao.AssemblyOrderId);
        builder.Property(ao => ao.AssemblyOrderId).UseIdentityAlwaysColumn();
        builder.Property(ao => ao.AssemblyCode).HasMaxLength(40).IsRequired();
        builder.Property(ao => ao.TotalCost).HasPrecision(18, 2).HasDefaultValue(0m);
        builder.Property(ao => ao.UnitCost)
            .HasPrecision(18, 2)
            .ValueGeneratedOnAddOrUpdate();
        builder.Property(ao => ao.Status).HasMaxLength(20).HasDefaultValue("Completed");
        builder.Property(ao => ao.Note).HasMaxLength(500);

        builder.HasIndex(ao => ao.AssemblyCode).IsUnique();
        builder.HasIndex(ao => ao.BundleVariantId);

        builder.HasOne(ao => ao.BundleVariant)
            .WithMany()
            .HasForeignKey(ao => ao.BundleVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ao => ao.CreatedByUser)
            .WithMany(u => u.AssemblyOrders)
            .HasForeignKey(ao => ao.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class AssemblyOrderItemConfiguration : IEntityTypeConfiguration<AssemblyOrderItem>
{
    public void Configure(EntityTypeBuilder<AssemblyOrderItem> builder)
    {
        builder.ToTable("assembly_order_items");
        builder.HasKey(aoi => aoi.AssemblyOrderItemId);
        builder.Property(aoi => aoi.AssemblyOrderItemId).UseIdentityAlwaysColumn();
        builder.Property(aoi => aoi.UnitCost).HasPrecision(18, 2).IsRequired();
        builder.Property(aoi => aoi.LineTotal)
            .HasPrecision(18, 2)
            .ValueGeneratedOnAddOrUpdate();

        builder.HasOne(aoi => aoi.AssemblyOrder)
            .WithMany(ao => ao.Items)
            .HasForeignKey(aoi => aoi.AssemblyOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(aoi => aoi.ComponentVariant)
            .WithMany(pv => pv.AssemblyOrderItems)
            .HasForeignKey(aoi => aoi.ComponentVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(aoi => aoi.AssemblyOrderId);
    }
}
