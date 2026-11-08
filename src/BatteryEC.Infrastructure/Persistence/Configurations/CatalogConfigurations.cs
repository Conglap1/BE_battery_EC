using BatteryEC.Domain.Entities.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BatteryEC.Infrastructure.Persistence.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.HasKey(c => c.CategoryId);
        builder.Property(c => c.CategoryId).UseIdentityAlwaysColumn();
        builder.Property(c => c.CategoryName).HasColumnName("category_name").HasMaxLength(100).IsRequired();
        builder.Property(c => c.Slug).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(500);
        builder.Property(c => c.IsActive).HasDefaultValue(true);
        builder.Property(c => c.DisplayOrder).HasColumnName("display_order").HasDefaultValue(0);

        builder.Ignore(c => c.Name);
        builder.Ignore(c => c.SortOrder);

        builder.HasIndex(c => c.Slug).IsUnique();

        builder.HasOne(c => c.ParentCategory)
            .WithMany(p => p.SubCategories)
            .HasForeignKey(c => c.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.HasKey(p => p.ProductId);
        builder.Property(p => p.ProductId).UseIdentityAlwaysColumn();
        builder.Property(p => p.ProductName).HasColumnName("product_name").HasMaxLength(200).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(220).IsRequired();
        builder.Property(p => p.Description).HasColumnType("text");
        builder.Property(p => p.IsActive).HasDefaultValue(true);
        builder.Property(p => p.Brand).HasColumnName("brand").HasMaxLength(100);
        builder.Property(p => p.ShortDescription).HasColumnName("short_description").HasMaxLength(500);
        builder.Property(p => p.IsFeatured).HasColumnName("is_featured").HasDefaultValue(false);

        builder.Ignore(p => p.Name);
        builder.Ignore(p => p.IsPublished);
        builder.Ignore(p => p.IsDeleted);

        builder.HasIndex(p => p.Slug).IsUnique();
        builder.HasIndex(p => p.CategoryId);

        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("product_variants");
        builder.HasKey(pv => pv.ProductVariantId);
        builder.Property(pv => pv.ProductVariantId).UseIdentityAlwaysColumn();
        builder.Property(pv => pv.SKU).HasMaxLength(50).IsRequired();
        builder.Property(pv => pv.VariantName).HasMaxLength(150).IsRequired();
        builder.Property(pv => pv.Price).HasColumnName("price").HasPrecision(18, 2).IsRequired();
        builder.Property(pv => pv.CostPrice).HasPrecision(18, 2).HasDefaultValue(0m);
        builder.Property(pv => pv.WeightGrams).HasDefaultValue(0);
        builder.Property(pv => pv.WarrantyMonths).HasDefaultValue(0);
        builder.Property(pv => pv.IsBundle).HasDefaultValue(false);
        builder.Property(pv => pv.IsActive).HasDefaultValue(true);
        builder.Property(pv => pv.OptionSummary).HasColumnName("option_summary").HasMaxLength(255);
        builder.Property(pv => pv.Unit).HasColumnName("unit").HasMaxLength(50).HasDefaultValue("Cái");
        builder.Property(pv => pv.ImageUrl).HasColumnName("image_url").HasMaxLength(500);

        builder.Ignore(pv => pv.SellingPrice);

        builder.HasIndex(pv => pv.SKU).IsUnique();
        builder.HasIndex(pv => pv.ProductId);

        builder.HasOne(pv => pv.Product)
            .WithMany(p => p.Variants)
            .HasForeignKey(pv => pv.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("product_images");
        builder.HasKey(pi => pi.ImageId);
        builder.Property(pi => pi.ImageId).HasColumnName("image_id").UseIdentityAlwaysColumn();
        builder.Property(pi => pi.ImageUrl).HasMaxLength(500).IsRequired();
        builder.Property(pi => pi.IsThumbnail).HasColumnName("is_thumbnail").HasDefaultValue(false);
        builder.Property(pi => pi.DisplayOrder).HasColumnName("display_order").HasDefaultValue(0);

        builder.Ignore(pi => pi.ProductImageId);
        builder.Ignore(pi => pi.IsPrimary);
        builder.Ignore(pi => pi.SortOrder);
        builder.Ignore(pi => pi.AltText);

        builder.HasOne(pi => pi.Product)
            .WithMany(p => p.Images)
            .HasForeignKey(pi => pi.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(pi => pi.ProductId);
    }
}

public class ProductAttributeConfiguration : IEntityTypeConfiguration<ProductAttribute>
{
    public void Configure(EntityTypeBuilder<ProductAttribute> builder)
    {
        builder.ToTable("attributes");
        builder.HasKey(a => a.AttributeId);
        builder.Property(a => a.AttributeId).UseIdentityAlwaysColumn();
        builder.Property(a => a.AttributeName).HasColumnName("attribute_name").HasMaxLength(100).IsRequired();
        builder.Property(a => a.AttributeCode).HasColumnName("attribute_code").HasMaxLength(50).IsRequired();

        builder.Ignore(a => a.Name);
        builder.Ignore(a => a.Code);

        builder.HasIndex(a => a.AttributeCode).IsUnique();
        builder.HasIndex(a => a.AttributeName).IsUnique();
    }
}

public class AttributeValueConfiguration : IEntityTypeConfiguration<AttributeValue>
{
    public void Configure(EntityTypeBuilder<AttributeValue> builder)
    {
        builder.ToTable("attribute_values");
        builder.HasKey(av => av.AttributeValueId);
        builder.Property(av => av.AttributeValueId).UseIdentityAlwaysColumn();
        builder.Property(av => av.ValueName).HasColumnName("value_name").HasMaxLength(100).IsRequired();

        builder.Ignore(av => av.Value);

        builder.HasOne(av => av.Attribute)
            .WithMany(a => a.Values)
            .HasForeignKey(av => av.AttributeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(av => new { av.AttributeId, av.ValueName }).IsUnique();
    }
}

public class ProductVariantAttributeConfiguration : IEntityTypeConfiguration<ProductVariantAttribute>
{
    public void Configure(EntityTypeBuilder<ProductVariantAttribute> builder)
    {
        builder.ToTable("product_variant_attributes");
        builder.HasKey(pva => new { pva.ProductVariantId, pva.AttributeValueId });

        builder.HasOne(pva => pva.ProductVariant)
            .WithMany(pv => pv.VariantAttributes)
            .HasForeignKey(pva => pva.ProductVariantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pva => pva.AttributeValue)
            .WithMany(av => av.VariantAttributes)
            .HasForeignKey(pva => pva.AttributeValueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(pva => new { pva.AttributeValueId, pva.ProductVariantId });
    }
}
