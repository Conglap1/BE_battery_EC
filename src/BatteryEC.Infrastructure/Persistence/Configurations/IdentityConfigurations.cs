using BatteryEC.Domain.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryEC.Infrastructure.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(r => r.RoleId);
        builder.Property(r => r.RoleId).UseIdentityAlwaysColumn();
        builder.Property(r => r.Name).HasMaxLength(30).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(200);
        builder.HasIndex(r => r.Name).IsUnique();
    }
}

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.UserId);
        builder.Property(u => u.UserId).UseIdentityAlwaysColumn();
        builder.Property(u => u.Email).HasMaxLength(254);
        builder.Property(u => u.Phone).HasMaxLength(20);
        builder.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(u => u.FullName).HasMaxLength(150).IsRequired();
        builder.Property(u => u.AvatarUrl).HasMaxLength(1000);
        builder.Property(u => u.RefreshToken).HasMaxLength(500);
        builder.Property(u => u.IsActive).HasDefaultValue(true);

        builder.HasIndex(u => u.Email).IsUnique().HasFilter("email IS NOT NULL");
        builder.HasIndex(u => u.Phone).IsUnique().HasFilter("phone IS NOT NULL");
    }
}

public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");
        builder.HasKey(ur => new { ur.UserId, ur.RoleId });

        builder.HasOne(ur => ur.User)
            .WithMany(u => u.UserRoles)
            .HasForeignKey(ur => ur.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ur => ur.Role)
            .WithMany(r => r.UserRoles)
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.ToTable("addresses");
        builder.HasKey(a => a.AddressId);
        builder.Property(a => a.AddressId).UseIdentityAlwaysColumn();
        builder.Property(a => a.RecipientName).HasMaxLength(150).IsRequired();
        builder.Property(a => a.RecipientPhone).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Province).HasMaxLength(100).IsRequired();
        builder.Property(a => a.District).HasMaxLength(100);
        builder.Property(a => a.Ward).HasMaxLength(100);
        builder.Property(a => a.AddressLine).HasMaxLength(300).IsRequired();
        builder.Property(a => a.PostalCode).HasMaxLength(20);
        builder.Property(a => a.IsDefault).HasDefaultValue(false);

        builder.HasOne(a => a.User)
            .WithMany(u => u.Addresses)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.UserId);
    }
}

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(c => c.CustomerId);
        builder.Property(c => c.CustomerId).UseIdentityAlwaysColumn();
        builder.Property(c => c.Phone).HasMaxLength(20).IsRequired();
        builder.Property(c => c.FullName).HasMaxLength(150);
        builder.Property(c => c.TotalSpent).HasPrecision(18, 2).HasDefaultValue(0m);

        builder.HasOne(c => c.User)
            .WithOne(u => u.Customer)
            .HasForeignKey<Customer>(c => c.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(c => c.Phone).IsUnique();
        builder.HasIndex(c => c.UserId).IsUnique().HasFilter("user_id IS NOT NULL");
    }
}
