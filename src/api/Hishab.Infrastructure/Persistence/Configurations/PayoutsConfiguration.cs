using Hishab.Domain.Couriers;
using Hishab.Domain.Finance;
using Hishab.Domain.Orders;
using Hishab.Infrastructure.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hishab.Infrastructure.Persistence.Configurations;

internal sealed class PayoutConfiguration : IEntityTypeConfiguration<Payout>
{
    public void Configure(EntityTypeBuilder<Payout> builder)
    {
        builder.ToTable("payouts");
        builder.Property(p => p.Provider).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.Reference).HasMaxLength(Payout.ReferenceMaxLength);
        builder.Property(p => p.Note).HasMaxLength(Payout.NoteMaxLength);
        builder.Ignore(p => p.Collected);
        builder.Ignore(p => p.DeliveryFees);
        builder.Ignore(p => p.CodFees);
        builder.Ignore(p => p.StatementNet);
        builder.Ignore(p => p.Difference);

        // Idempotent entry: one statement per courier and reference. The service checks first for a
        // friendly message; this index decides when two people enter the same statement at once.
        builder.HasIndex(p => new { p.TenantId, p.Provider, p.Reference }).IsUnique().HasDatabaseName(PayoutService.ReferenceIndex);
        builder.HasIndex(p => new { p.TenantId, p.PaidOn, p.RecordedAt }); // the list: newest paid first

        builder.OwnsMany(p => p.Lines, line =>
        {
            line.ToTable("payout_lines");
            line.WithOwner().HasForeignKey("PayoutId");
            line.HasKey(l => l.Id);
            line.Property(l => l.Id).ValueGeneratedNever();
            line.Property(l => l.ConsignmentId).HasMaxLength(Shipment.ConsignmentMaxLength);
            line.Property(l => l.Match).HasConversion<string>().HasMaxLength(24);
            line.Ignore(l => l.SettlesOrderId);
            line.Ignore(l => l.Shortfall);
            line.HasIndex("PayoutId", nameof(PayoutLine.Sequence)).IsUnique();

            // Lines have no TenantId (query them through payouts); the foreign keys keep them pointing
            // at real orders and shipments, which are never deleted.
            line.HasOne<Order>().WithMany().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Restrict);
            line.HasOne<Shipment>().WithMany().HasForeignKey(l => l.SettledShipmentId).OnDelete(DeleteBehavior.Restrict);

            // A parcel is paid once: of two payouts settling it, the second save fails on this index.
            line.HasIndex(l => l.SettledShipmentId)
                .IsUnique()
                .HasFilter("settled_shipment_id IS NOT NULL")
                .HasDatabaseName(PayoutService.SettledShipmentIndex);
        });
        builder.Navigation(p => p.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
