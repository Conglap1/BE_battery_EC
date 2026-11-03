using BatteryEC.Domain.Entities.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryEC.Infrastructure.Persistence.Configurations;

public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("reviews");
        builder.HasKey(r => r.ReviewId);
        builder.Property(r => r.ReviewId).UseIdentityAlwaysColumn();

        builder.Property(r => r.Rating).IsRequired();
        builder.Property(r => r.Comment).HasMaxLength(1000);
        builder.Property(r => r.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(r => r.ProductId);
        builder.HasIndex(r => r.UserId);
        builder.HasIndex(r => r.OrderItemId).IsUnique();

        builder.HasOne(r => r.User)
            .WithMany(u => u.Reviews)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Product)
            .WithMany(p => p.Reviews)
            .HasForeignKey(r => r.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.OrderItem)
            .WithMany(oi => oi.Reviews)
            .HasForeignKey(r => r.OrderItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ShopExpenseConfiguration : IEntityTypeConfiguration<ShopExpense>
{
    public void Configure(EntityTypeBuilder<ShopExpense> builder)
    {
        builder.ToTable("shop_expenses");
        builder.HasKey(se => se.ExpenseId);
        builder.Property(se => se.ExpenseId).UseIdentityAlwaysColumn();

        builder.Property(se => se.ExpenseCode).HasMaxLength(40).IsRequired();
        builder.Property(se => se.ExpenseCategory).HasMaxLength(100).IsRequired();
        builder.Property(se => se.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(se => se.Note).HasMaxLength(500);
        builder.Property(se => se.PaidAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Ignore(se => se.Category);
        builder.Ignore(se => se.CreatedByUserId);
        builder.Ignore(se => se.PaymentMethod);

        builder.HasIndex(se => se.ExpenseCode).IsUnique();
        builder.HasIndex(se => se.ExpenseCategory);
        builder.HasIndex(se => se.PaidAt);

        builder.HasOne(se => se.PaidByUser)
            .WithMany(u => u.ShopExpenses)
            .HasForeignKey(se => se.PaidByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class StoreSettingConfiguration : IEntityTypeConfiguration<StoreSetting>
{
    public void Configure(EntityTypeBuilder<StoreSetting> builder)
    {
        builder.ToTable("store_settings");
        builder.HasKey(ss => ss.SettingId);
        builder.Property(ss => ss.SettingId).UseIdentityAlwaysColumn();

        builder.Property(ss => ss.SettingKey).HasMaxLength(100).IsRequired();
        builder.Property(ss => ss.SettingValue).HasColumnType("text").IsRequired();
        builder.Property(ss => ss.Description).HasMaxLength(255);
        builder.Property(ss => ss.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasIndex(ss => ss.SettingKey).IsUnique();
    }
}
