using Hishab.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hishab.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.Property(p => p.Name).HasMaxLength(Product.NameMaxLength);
        builder.Property(p => p.Description).HasMaxLength(Product.DescriptionMaxLength);
        builder.Ignore(p => p.ActiveVariants);

        // The aggregate owns its variants: EF writes the private list; nobody else adds to it.
        builder.HasMany(p => p.Variants).WithOne().HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(p => p.Variants).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(p => new { p.TenantId, p.Name }); // list ordered by name per shop
    }
}

internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("product_variants");
        builder.Property(v => v.Sku).HasMaxLength(ProductVariant.SkuMaxLength);
        builder.Property(v => v.Size).HasMaxLength(ProductVariant.OptionMaxLength);
        builder.Property(v => v.Colour).HasMaxLength(ProductVariant.OptionMaxLength);
        builder.Ignore(v => v.Margin);

        // One active variant per SKU per shop; deleted variants free their SKU for reuse. The
        // database enforces it, so two parallel creates cannot both take the same SKU.
        builder.HasIndex(v => new { v.TenantId, v.Sku })
            .IsUnique()
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ux_product_variants_tenant_sku_active");
    }
}
