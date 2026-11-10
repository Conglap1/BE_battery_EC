using BatteryEC.Domain.Entities.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryEC.Infrastructure.Persistence.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers");
        builder.HasKey(s => s.SupplierId);
        builder.Property(s => s.SupplierId).UseIdentityAlwaysColumn();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.ContactName).HasMaxLength(150);
        builder.Property(s => s.Phone).HasMaxLength(20);
        builder.Property(s => s.Email).HasMaxLength(254);
        builder.Property(s => s.Address).HasMaxLength(500);
        builder.Property(s => s.TaxCode).HasMaxLength(50);
        builder.Property(s => s.Notes).HasMaxLength(1000);
        builder.Property(s => s.IsActive).HasDefaultValue(true);
    }
}

public class PurchaseReceiptConfiguration : IEntityTypeConfiguration<PurchaseReceipt>
{
    public void Configure(EntityTypeBuilder<PurchaseReceipt> builder)
    {
        builder.ToTable("purchase_receipts");
        builder.HasKey(pr => pr.PurchaseReceiptId);
        builder.Property(pr => pr.PurchaseReceiptId).UseIdentityAlwaysColumn();
        builder.Property(pr => pr.ReceiptCode).HasMaxLength(40).IsRequired();
        builder.Property(pr => pr.ReceiptStatus).HasMaxLength(20).HasDefaultValue("Draft");
        builder.Property(pr => pr.TotalAmount).HasPrecision(18, 2).HasDefaultValue(0m);
        builder.Property(pr => pr.PaidAmount).HasPrecision(18, 2).HasDefaultValue(0m);
        builder.Property(pr => pr.PaymentStatus).HasMaxLength(30).HasDefaultValue("Unpaid");
        builder.Property(pr => pr.SupplierInvoiceNo).HasMaxLength(100);
        builder.Property(pr => pr.Note).HasMaxLength(1000);

        builder.HasIndex(pr => pr.ReceiptCode).IsUnique();

        builder.HasOne(pr => pr.Supplier)
            .WithMany(s => s.Receipts)
            .HasForeignKey(pr => pr.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(pr => pr.CreatedByUser)
            .WithMany(u => u.PurchaseReceipts)
            .HasForeignKey(pr => pr.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PurchaseReceiptItemConfiguration : IEntityTypeConfiguration<PurchaseReceiptItem>
{
    public void Configure(EntityTypeBuilder<PurchaseReceiptItem> builder)
    {
        builder.ToTable("purchase_receipt_items");
        builder.HasKey(pri => pri.PurchaseReceiptItemId);
        builder.Property(pri => pri.PurchaseReceiptItemId).UseIdentityAlwaysColumn();
        builder.Property(pri => pri.UnitCost).HasPrecision(18, 2).IsRequired();
        builder.Property(pri => pri.LineTotal)
            .HasPrecision(18, 2)
            .ValueGeneratedOnAddOrUpdate();

        builder.HasOne(pri => pri.PurchaseReceipt)
            .WithMany(pr => pr.Items)
            .HasForeignKey(pri => pri.PurchaseReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pri => pri.ProductVariant)
            .WithMany(pv => pv.PurchaseReceiptItems)
            .HasForeignKey(pri => pri.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(pri => new { pri.PurchaseReceiptId, pri.ProductVariantId }).IsUnique();
    }
}

public class SupplierPaymentConfiguration : IEntityTypeConfiguration<SupplierPayment>
{
    public void Configure(EntityTypeBuilder<SupplierPayment> builder)
    {
        builder.ToTable("supplier_payments");
        builder.HasKey(sp => sp.SupplierPaymentId);
        builder.Property(sp => sp.SupplierPaymentId).UseIdentityAlwaysColumn();
        builder.Property(sp => sp.PaymentCode).HasMaxLength(40).IsRequired();
        builder.Property(sp => sp.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(sp => sp.PaymentMethod).HasMaxLength(30).HasDefaultValue("BankTransfer");
        builder.Property(sp => sp.ReferenceNumber).HasMaxLength(100);
        builder.Property(sp => sp.Note).HasMaxLength(500);

        builder.HasIndex(sp => sp.PaymentCode).IsUnique();
        builder.HasIndex(sp => sp.PurchaseReceiptId);

        builder.HasOne(sp => sp.PurchaseReceipt)
            .WithMany(pr => pr.Payments)
            .HasForeignKey(sp => sp.PurchaseReceiptId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(sp => sp.CreatedByUser)
            .WithMany(u => u.SupplierPayments)
            .HasForeignKey(sp => sp.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
