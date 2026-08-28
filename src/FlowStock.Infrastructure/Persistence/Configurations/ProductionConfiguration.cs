using FlowStock.Domain.Inventory;
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

public class ProductionOrderConfiguration : IEntityTypeConfiguration<ProductionOrder>
{
    public void Configure(EntityTypeBuilder<ProductionOrder> builder)
    {
        builder.ToTable("ProductionOrders", table =>
        {
            table.HasCheckConstraint("CK_ProductionOrders_PlannedQuantity_Positive", "\"PlannedQuantity\" > 0");
            table.HasCheckConstraint("CK_ProductionOrders_ProducedQuantity_NonNegative", "\"ProducedQuantity\" >= 0");
        });

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Number).IsRequired().HasMaxLength(30);
        builder.HasIndex(o => o.Number).IsUnique();

        builder.Property(o => o.PlannedQuantity).IsRequired().HasPrecision(18, 4);
        builder.Property(o => o.ProducedQuantity).IsRequired().HasPrecision(18, 4);

        // Stored by name, so reordering the enum can never reinterpret an existing order.
        builder.Property(o => o.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(o => o.Status);

        builder.Property(o => o.Notes).HasMaxLength(1000);
        builder.Property(o => o.CreatedAt).IsRequired();

        builder.HasOne(o => o.Product)
            .WithMany()
            .HasForeignKey(o => o.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // The recipe version an order was built from must stay readable for as long as the order does.
        builder.HasOne(o => o.BillOfMaterial)
            .WithMany()
            .HasForeignKey(o => o.BillOfMaterialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.ProductionLocation)
            .WithMany()
            .HasForeignKey(o => o.ProductionLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.OutputLocation)
            .WithMany()
            .HasForeignKey(o => o.OutputLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.OutputBatch)
            .WithMany()
            .HasForeignKey(o => o.OutputBatchId)
            .OnDelete(DeleteBehavior.Restrict);

        // The movements an order posted point back at it. Declared from this side because the
        // inventory module knows nothing about production beyond the foreign key itself.
        builder.HasMany<StockMovement>()
            .WithOne()
            .HasForeignKey(m => m.ProductionOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductionOrderMaterialConfiguration : IEntityTypeConfiguration<ProductionOrderMaterial>
{
    public void Configure(EntityTypeBuilder<ProductionOrderMaterial> builder)
    {
        builder.ToTable("ProductionOrderMaterials", table =>
        {
            table.HasCheckConstraint("CK_ProductionOrderMaterials_RequiredQuantity_Positive",
                "\"RequiredQuantity\" > 0");
            table.HasCheckConstraint("CK_ProductionOrderMaterials_ConsumedQuantity_NonNegative",
                "\"ConsumedQuantity\" >= 0");
        });

        builder.HasKey(m => m.Id);

        builder.Property(m => m.RequiredQuantity).IsRequired().HasPrecision(18, 4);
        builder.Property(m => m.ConsumedQuantity).IsRequired().HasPrecision(18, 4);

        // A material appears once per order; its quantity is a single number.
        builder.HasIndex(m => new { m.ProductionOrderId, m.ComponentProductId }).IsUnique();

        // A material line has no life of its own outside its order.
        builder.HasOne(m => m.ProductionOrder)
            .WithMany(o => o.Materials)
            .HasForeignKey(m => m.ProductionOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.ComponentProduct)
            .WithMany()
            .HasForeignKey(m => m.ComponentProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.UnitOfMeasure)
            .WithMany()
            .HasForeignKey(m => m.UnitOfMeasureId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.Batch)
            .WithMany()
            .HasForeignKey(m => m.BatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
