using Hishab.Domain.Couriers;
using Hishab.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hishab.Infrastructure.Persistence.Configurations;

internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("shipments");
        builder.Property(s => s.Provider).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.MerchantReference).HasMaxLength(Shipment.ReferenceMaxLength);
        builder.Property(s => s.ConsignmentId).HasMaxLength(Shipment.ConsignmentMaxLength);
        builder.Property(s => s.TrackingCode).HasMaxLength(Shipment.ConsignmentMaxLength);
        builder.Property(s => s.Error).HasMaxLength(Shipment.ErrorMaxLength);
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasOne<Order>().WithMany().HasForeignKey(s => s.OrderId).OnDelete(DeleteBehavior.Restrict);

        // One row per order and courier: retries reuse it, so the reference never changes.
        builder.HasIndex(s => new { s.TenantId, s.OrderId, s.Provider }).IsUnique();

        // At most one live booking per order, whatever the courier: two parallel requests (or two
        // couriers) cannot both have a parcel in flight for the same order.
        builder.HasIndex(s => new { s.TenantId, s.OrderId })
            .IsUnique()
            .HasFilter("state IN ('Requested', 'Booked')")
            .HasDatabaseName("ux_shipments_one_live_per_order");
    }
}
