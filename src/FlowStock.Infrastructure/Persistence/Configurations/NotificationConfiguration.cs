using FlowStock.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowStock.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        builder.HasKey(n => n.Id);

        // Stored by name: readable in the database and immune to enum reordering.
        builder.Property(n => n.Type).IsRequired().HasMaxLength(32).HasConversion<string>();

        builder.Property(n => n.Message).IsRequired().HasMaxLength(1000);
        builder.Property(n => n.OccurredAt).IsRequired();
        builder.Property(n => n.CreatedAt).IsRequired();

        // One notification per event. This is what makes a scan that runs every quarter of an hour
        // — or two API instances running it at once — raise the same expired lot exactly once.
        builder.Property(n => n.EventKey).IsRequired().HasMaxLength(200);
        builder.HasIndex(n => n.EventKey).IsUnique();

        // An inbox is read newest first, and usually unread first.
        builder.HasIndex(n => n.OccurredAt);
        builder.HasIndex(n => n.IsRead);

        // The references are plain ids, not navigations: a notification points at what it is
        // about without tying the notification module to those entities' lifetimes.
        builder.HasIndex(n => n.ProductId);
        builder.HasIndex(n => n.ProductionOrderId);
    }
}
