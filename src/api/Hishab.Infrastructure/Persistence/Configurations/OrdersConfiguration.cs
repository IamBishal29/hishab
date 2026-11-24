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
        // The order list reads newest first and pages by (created_at, number): with the sort key at the
        // end of the index, Postgres walks it backwards and stops after one page (no sort, no OFFSET).
        builder.HasIndex(o => new { o.TenantId, o.CreatedAt, o.Number });
        builder.HasIndex(o => new { o.TenantId, o.Status, o.CreatedAt, o.Number }); // "Confirmed orders, newest first"
        // "This customer's orders, newest first" (the customer page); also serves the history counts.
        builder.HasIndex(o => new { o.TenantId, o.CustomerId, o.CreatedAt, o.Number });
        builder.HasIndex(o => new { o.TenantId, o.Number }).IsUnique(); // "#1043" means one order per shop

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

            // A copy of the order's tenant (stamped by SaveChanges), so reports can find "this shop's
            // deliveries between two dates" in one index range instead of every shop's.
            change.Property<Guid>(HishabDbContext.OwnedTenantIdProperty);
            change.HasIndex(HishabDbContext.OwnedTenantIdProperty, nameof(OrderStatusChange.To), nameof(OrderStatusChange.ChangedAt));
        });
        builder.Navigation(o => o.History).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<Hishab.Domain.Common.IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<Hishab.Domain.Common.IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_keys");

        // The guarantee: one record per key per shop. A parallel request with the same key waits on
        // this index until the first transaction commits, then fails and replays the stored result.
        builder.HasKey(r => new { r.TenantId, r.Key });
        builder.Property(r => r.Key).HasMaxLength(Hishab.Domain.Common.IdempotencyRecord.KeyMaxLength);
        builder.Property(r => r.Operation).HasMaxLength(Hishab.Domain.Common.IdempotencyRecord.OperationMaxLength);
        builder.Property(r => r.RequestHash).HasMaxLength(64).IsFixedLength();
    }
}
