using FlowStock.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowStock.Infrastructure.Persistence.Configurations;

public class StockConfiguration : IEntityTypeConfiguration<Stock>
{
    public void Configure(EntityTypeBuilder<Stock> builder)
    {
        builder.ToTable("Stocks", table =>
        {
            // The last line of defence behind the application rule: even a bug must not be able
            // to leave a negative balance in the database (docs/PLAN.md, sections 27 and 28).
            table.HasCheckConstraint("CK_Stocks_Quantity_NotNegative", "\"Quantity\" >= 0");
            table.HasCheckConstraint("CK_Stocks_ReservedQuantity_NotNegative", "\"ReservedQuantity\" >= 0");
            table.HasCheckConstraint(
                "CK_Stocks_ReservedQuantity_NotAboveQuantity", "\"ReservedQuantity\" <= \"Quantity\"");
        });

        builder.HasKey(s => s.Id);

        // Decimal, never floating point (CLAUDE.md, rule 4).
        builder.Property(s => s.Quantity).IsRequired().HasPrecision(18, 4);
        builder.Property(s => s.ReservedQuantity).IsRequired().HasPrecision(18, 4);
        builder.Property(s => s.CreatedAt).IsRequired();

        // Derived from the two stored quantities; never a column.
        builder.Ignore(s => s.AvailableQuantity);

        // One balance per product per location — this is what makes the row lock in
        // LockStockAsync address exactly one row.
        builder.HasIndex(s => new { s.ProductId, s.LocationId }).IsUnique();
        builder.HasIndex(s => s.LocationId);

        builder.HasOne(s => s.Product)
            .WithMany()
            .HasForeignKey(s => s.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Location)
            .WithMany()
            .HasForeignKey(s => s.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Number).IsRequired().HasMaxLength(32);
        builder.Property(m => m.Reason).HasMaxLength(1000);
        builder.Property(m => m.CreatedAt).IsRequired();

        // Stored by name: readable in the database and immune to enum reordering.
        builder.Property(m => m.MovementType).IsRequired().HasMaxLength(32).HasConversion<string>();
        builder.Property(m => m.Status).IsRequired().HasMaxLength(16).HasConversion<string>();

        builder.HasIndex(m => m.Number).IsUnique();
        builder.HasIndex(m => m.Status);
        builder.HasIndex(m => m.CreatedAt);

        // Traceability reads both ways from here: which movements a run posted, and which runs a
        // material ended up in (docs/PLAN.md, section 19). The relationship itself is configured
        // from the production side, which is the module that knows about orders.
        builder.HasIndex(m => m.ProductionOrderId);

        // Locations are immutable history once a movement is confirmed, so they are never deleted.
        builder.HasOne(m => m.SourceLocation)
            .WithMany()
            .HasForeignKey(m => m.SourceLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.DestinationLocation)
            .WithMany()
            .HasForeignKey(m => m.DestinationLocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class StockMovementLineConfiguration : IEntityTypeConfiguration<StockMovementLine>
{
    public void Configure(EntityTypeBuilder<StockMovementLine> builder)
    {
        builder.ToTable("StockMovementLines", table =>
            // docs/PLAN.md, section 27: movement lines must have positive quantities. Direction
            // comes from the document's endpoints, never from the sign of a line.
            table.HasCheckConstraint("CK_StockMovementLines_Quantity_Positive", "\"Quantity\" > 0"));

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Quantity).IsRequired().HasPrecision(18, 4);

        builder.HasIndex(l => l.ProductId);

        // A line has no life of its own outside its document.
        builder.HasOne(l => l.StockMovement)
            .WithMany(m => m.Lines)
            .HasForeignKey(l => l.StockMovementId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.Product)
            .WithMany()
            .HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.UnitOfMeasure)
            .WithMany()
            .HasForeignKey(l => l.UnitOfMeasureId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
