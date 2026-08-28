using FlowStock.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowStock.Infrastructure.Persistence.Configurations;

public class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        builder.ToTable("UnitsOfMeasure");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Code).IsRequired().HasMaxLength(16);
        builder.Property(u => u.Name).IsRequired().HasMaxLength(64);
        builder.Property(u => u.IsActive).IsRequired();
        builder.Property(u => u.CreatedAt).IsRequired();

        builder.HasIndex(u => u.Code).IsUnique();
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Sku).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.IsActive).IsRequired();
        builder.Property(p => p.CreatedAt).IsRequired();

        // Stored by name: readable in the database and immune to enum reordering.
        builder.Property(p => p.ProductType)
            .IsRequired()
            .HasMaxLength(32)
            .HasConversion<string>();

        // docs/PLAN.md, section 27: Products.Sku UNIQUE.
        builder.HasIndex(p => p.Sku).IsUnique();
        builder.HasIndex(p => p.ProductType);

        // A unit in use must not disappear from under the products that reference it.
        builder.HasOne(p => p.UnitOfMeasure)
            .WithMany(u => u.Products)
            .HasForeignKey(p => p.UnitOfMeasureId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
