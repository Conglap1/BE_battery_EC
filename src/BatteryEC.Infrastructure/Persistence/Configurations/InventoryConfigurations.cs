using BatteryEC.Domain.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryEC.Infrastructure.Persistence.Configurations;

public class InventoryConfiguration : IEntityTypeConfiguration<BatteryEC.Domain.Entities.Inventory.Inventory>
{
    public void Configure(EntityTypeBuilder<BatteryEC.Domain.Entities.Inventory.Inventory> builder)
    {
        builder.ToTable("inventory");
        builder.HasKey(i => i.ProductVariantId);

        builder.Property(i => i.QuantityOnHand).HasDefaultValue(0);
        builder.Property(i => i.QuantityReserved).HasDefaultValue(0);
        builder.Property(i => i.ReorderLevel).HasDefaultValue(0);
        builder.Property(i => i.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(i => i.Version).HasDefaultValue(1);

        builder.Ignore(i => i.AvailableStock);

        builder.HasOne(i => i.ProductVariant)
            .WithOne(pv => pv.Inventory)
            .HasForeignKey<BatteryEC.Domain.Entities.Inventory.Inventory>(i => i.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> builder)
    {
        builder.ToTable("inventory_transactions");
        builder.HasKey(it => it.InventoryTransactionId);
        builder.Property(it => it.InventoryTransactionId).UseIdentityAlwaysColumn();

        builder.Property(it => it.TransactionType).HasMaxLength(30).IsRequired();
        builder.Property(it => it.ReferenceType).HasMaxLength(50).IsRequired();
        builder.Property(it => it.ReferenceId).IsRequired();
        builder.Property(it => it.UnitCost).HasPrecision(18, 2).HasDefaultValue(0m);
        builder.Property(it => it.Note).HasMaxLength(500);
        builder.Property(it => it.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(it => it.ProductVariantId);
        builder.HasIndex(it => new { it.ReferenceType, it.ReferenceId });
        builder.HasIndex(it => it.CreatedAt);

        builder.HasOne(it => it.ProductVariant)
            .WithMany(pv => pv.InventoryTransactions)
            .HasForeignKey(it => it.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(it => it.CreatedByUser)
            .WithMany(u => u.InventoryTransactions)
            .HasForeignKey(it => it.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
