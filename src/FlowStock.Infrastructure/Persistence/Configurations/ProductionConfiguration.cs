using FlowStock.Domain.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowStock.Infrastructure.Persistence.Configurations;

public class BillOfMaterialConfiguration : IEntityTypeConfiguration<BillOfMaterial>
{
    public void Configure(EntityTypeBuilder<BillOfMaterial> builder)
    {
        builder.ToTable("BillsOfMaterial", table =>
            table.HasCheckConstraint("CK_BillsOfMaterial_OutputQuantity_Positive", "\"OutputQuantity\" > 0"));

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Version).IsRequired();
        builder.Property(b => b.OutputQuantity).IsRequired().HasPrecision(18, 4);
        builder.Property(b => b.Name).HasMaxLength(200);
        builder.Property(b => b.Description).HasMaxLength(1000);
        builder.Property(b => b.IsActive).IsRequired();
        builder.Property(b => b.CreatedAt).IsRequired();

        // Versions of one product's recipe are numbered, and a number is never reused.
        builder.HasIndex(b => new { b.ProductId, b.Version }).IsUnique();

        // At most one version of a product's recipe is in force at a time. A filtered unique index
        // says so in the database, not only in the service that maintains it.
        builder.HasIndex(b => b.ProductId)
            .IsUnique()
            .HasFilter("\"IsActive\"")
            .HasDatabaseName("IX_BillsOfMaterial_ProductId_Active");

        builder.HasOne(b => b.Product)
            .WithMany()
            .HasForeignKey(b => b.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class BillOfMaterialItemConfiguration : IEntityTypeConfiguration<BillOfMaterialItem>
{
    public void Configure(EntityTypeBuilder<BillOfMaterialItem> builder)
    {
        builder.ToTable("BillOfMaterialItems", table =>
            table.HasCheckConstraint("CK_BillOfMaterialItems_Quantity_Positive", "\"Quantity\" > 0"));

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Quantity).IsRequired().HasPrecision(18, 4);

        // A component appears once per recipe; its quantity is a single number.
        builder.HasIndex(i => new { i.BillOfMaterialId, i.ComponentProductId }).IsUnique();

        // An item has no life of its own outside its recipe.
        builder.HasOne(i => i.BillOfMaterial)
            .WithMany(b => b.Items)
            .HasForeignKey(i => i.BillOfMaterialId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.ComponentProduct)
            .WithMany()
            .HasForeignKey(i => i.ComponentProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.UnitOfMeasure)
            .WithMany()
            .HasForeignKey(i => i.UnitOfMeasureId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
