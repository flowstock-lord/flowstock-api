using FlowStock.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowStock.Infrastructure.Persistence.Configurations;

public class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("Warehouses");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Code).IsRequired().HasMaxLength(32);
        builder.Property(w => w.Name).IsRequired().HasMaxLength(200);
        builder.Property(w => w.Description).HasMaxLength(1000);
        builder.Property(w => w.IsActive).IsRequired();
        builder.Property(w => w.CreatedAt).IsRequired();

        // Stored by name: readable in the database and immune to enum reordering.
        builder.Property(w => w.WarehouseType)
            .IsRequired()
            .HasMaxLength(32)
            .HasConversion<string>();

        // docs/PLAN.md, section 27: Warehouses.Code UNIQUE.
        builder.HasIndex(w => w.Code).IsUnique();
    }
}

public class StorageLocationConfiguration : IEntityTypeConfiguration<StorageLocation>
{
    public void Configure(EntityTypeBuilder<StorageLocation> builder)
    {
        builder.ToTable("StorageLocations");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Code).IsRequired().HasMaxLength(32);
        builder.Property(l => l.Name).IsRequired().HasMaxLength(200);
        builder.Property(l => l.Description).HasMaxLength(1000);
        builder.Property(l => l.IsActive).IsRequired();
        builder.Property(l => l.CreatedAt).IsRequired();

        // A-01 may exist in several warehouses, but only once inside one.
        builder.HasIndex(l => new { l.WarehouseId, l.Code }).IsUnique();

        // A location belongs to exactly one warehouse and the warehouse cannot be deleted
        // out from under it (docs/PLAN.md, section 27).
        builder.HasOne(l => l.Warehouse)
            .WithMany(w => w.Locations)
            .HasForeignKey(l => l.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
