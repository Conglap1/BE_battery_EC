using BatteryEC.Domain.Entities.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryEC.Infrastructure.Persistence.Configurations;

public class RepairTicketConfiguration : IEntityTypeConfiguration<RepairTicket>
{
    public void Configure(EntityTypeBuilder<RepairTicket> builder)
    {
        builder.ToTable("repair_tickets");
        builder.HasKey(rt => rt.RepairTicketId);
        builder.Property(rt => rt.RepairTicketId).UseIdentityAlwaysColumn();

        builder.Property(rt => rt.TicketCode).HasMaxLength(40).IsRequired();
        builder.Property(rt => rt.CustomerName).HasMaxLength(100).IsRequired();
        builder.Property(rt => rt.CustomerPhone).HasMaxLength(20).IsRequired();
        builder.Property(rt => rt.DeviceModel).HasMaxLength(150).IsRequired();
        builder.Property(rt => rt.SerialOrPinCode).HasMaxLength(100);
        builder.Property(rt => rt.IssueDescription).HasMaxLength(500).IsRequired();
        builder.Property(rt => rt.SolutionNote).HasMaxLength(500);
        builder.Property(rt => rt.EstimatedCost).HasPrecision(18, 2).HasDefaultValue(0m);
        builder.Property(rt => rt.FinalCost).HasPrecision(18, 2).HasDefaultValue(0m);
        builder.Property(rt => rt.WarrantyMonths).HasDefaultValue(0);
        builder.Property(rt => rt.TicketStatus).HasMaxLength(30).HasDefaultValue("Received");
        builder.Property(rt => rt.PaymentStatus).HasMaxLength(20).HasDefaultValue("Unpaid");
        builder.Property(rt => rt.PaymentMethod).HasMaxLength(30);
        builder.Property(rt => rt.ReceivedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Ignore(rt => rt.TechnicianId);
        builder.Ignore(rt => rt.PaidAmount);

        builder.HasIndex(rt => rt.TicketCode).IsUnique();
        builder.HasIndex(rt => rt.CustomerId);
        builder.HasIndex(rt => rt.TicketStatus);
        builder.HasIndex(rt => rt.TechnicianUserId);
        builder.HasIndex(rt => rt.ReceivedAt);

        builder.HasOne(rt => rt.Customer)
            .WithMany(c => c.RepairTickets)
            .HasForeignKey(rt => rt.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(rt => rt.CreatedByUser)
            .WithMany(u => u.CreatedRepairTickets)
            .HasForeignKey(rt => rt.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(rt => rt.Technician)
            .WithMany(u => u.TechnicianRepairTickets)
            .HasForeignKey(rt => rt.TechnicianUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class RepairTicketItemConfiguration : IEntityTypeConfiguration<RepairTicketItem>
{
    public void Configure(EntityTypeBuilder<RepairTicketItem> builder)
    {
        builder.ToTable("repair_ticket_items");
        builder.HasKey(rti => rti.RepairTicketItemId);
        builder.Property(rti => rti.RepairTicketItemId).UseIdentityAlwaysColumn();

        builder.Property(rti => rti.Quantity).IsRequired();
        builder.Property(rti => rti.UnitPrice).HasPrecision(18, 2).HasDefaultValue(0m);
        builder.Property(rti => rti.LineTotal).HasPrecision(18, 2).ValueGeneratedOnAddOrUpdate();

        builder.HasIndex(rti => rti.RepairTicketId);
        builder.HasIndex(rti => rti.ProductVariantId);

        builder.HasOne(rti => rti.RepairTicket)
            .WithMany(rt => rt.Items)
            .HasForeignKey(rti => rti.RepairTicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(rti => rti.ProductVariant)
            .WithMany(pv => pv.RepairTicketItems)
            .HasForeignKey(rti => rti.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
