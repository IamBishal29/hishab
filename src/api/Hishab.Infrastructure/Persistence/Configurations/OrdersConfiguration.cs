using Hishab.Domain.Customers;
using Hishab.Domain.Orders;
using Hishab.Infrastructure.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hishab.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.Property(o => o.Source).HasConversion<string>().HasMaxLength(16);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(o => o.DeliveryAddress).HasMaxLength(Order.AddressMaxLength).IsPersonalData(PersonalDataKind.Text);
        builder.Property(o => o.Note).HasMaxLength(Order.NoteMaxLength);
        builder.Ignore(o => o.Subtotal);
        builder.Ignore(o => o.Total);
        builder.Ignore(o => o.CodAmount);

        builder.HasOne<Customer>().WithMany().HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(o => new { o.TenantId, o.CreatedAt }); // newest first per shop; #16 adds filter indexes

        // Optimistic concurrency: every transition updates this row, so of two packers changing
        // the same order, the second save fails instead of silently overwriting the first.
        builder.Property<uint>("Version").IsRowVersion();

        // Lines and history are part of the aggregate: their own tables, always loaded with the order.
        builder.OwnsMany(o => o.Lines, line =>
        {
            line.ToTable("order_lines");
            line.WithOwner().HasForeignKey("OrderId");
            line.HasKey(l => l.Id);
            line.Property(l => l.Id).ValueGeneratedNever();
            line.Property(l => l.ProductName).HasMaxLength(OrderLine.NameMaxLength);
            line.Property(l => l.VariantLabel).HasMaxLength(OrderLine.LabelMaxLength);
            line.Property(l => l.Sku).HasMaxLength(64);
            line.Ignore(l => l.LineTotal);
        });
        builder.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(o => o.History, change =>
        {
            change.ToTable("order_status_changes");
            change.WithOwner().HasForeignKey("OrderId");
            change.HasKey(c => c.Id);
            change.Property(c => c.Id).ValueGeneratedNever();
            change.Property(c => c.From).HasConversion<string>().HasMaxLength(16);
            change.Property(c => c.To).HasConversion<string>().HasMaxLength(16);
            change.Property(c => c.Note).HasMaxLength(Order.NoteMaxLength);
            change.HasIndex("OrderId", nameof(OrderStatusChange.Sequence)).IsUnique(); // one step per position
        });
        builder.Navigation(o => o.History).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
