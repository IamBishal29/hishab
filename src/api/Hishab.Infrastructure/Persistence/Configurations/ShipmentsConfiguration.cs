using Hishab.Domain.Couriers;
using Hishab.Domain.Orders;
using Hishab.Infrastructure.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hishab.Infrastructure.Persistence.Configurations;

internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        // A destination is whole or absent (the entity enforces it too; this guards raw SQL fixes).
        builder.ToTable("shipments", t => t.HasCheckConstraint(
            "ck_shipments_destination_complete",
            "(destination_city_id IS NULL) = (destination_zone_id IS NULL) AND (destination_area_id IS NULL OR destination_zone_id IS NOT NULL)"));
        builder.Property(s => s.Provider).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.MerchantReference).HasMaxLength(Shipment.ReferenceMaxLength);
        builder.Property(s => s.ConsignmentId).HasMaxLength(Shipment.ConsignmentMaxLength);
        builder.Property(s => s.TrackingCode).HasMaxLength(Shipment.ConsignmentMaxLength);
        builder.Property(s => s.Error).HasMaxLength(Shipment.ErrorMaxLength);
        builder.Property(s => s.ParcelStatus).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.CourierStatus).HasMaxLength(Shipment.ConsignmentMaxLength);
        builder.Property(s => s.StatusCheckedAt).IsNotAudited(); // changes on every sync: not history
        builder.Ignore(s => s.IsTracking);
        builder.Ignore(s => s.Destination);
        builder.Property(s => s.DestinationCityId).HasMaxLength(CourierDestination.IdMaxLength);
        builder.Property(s => s.DestinationZoneId).HasMaxLength(CourierDestination.IdMaxLength);
        builder.Property(s => s.DestinationAreaId).HasMaxLength(CourierDestination.IdMaxLength);
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasOne<Order>().WithMany().HasForeignKey(s => s.OrderId).OnDelete(DeleteBehavior.Restrict);

        // One row per order and courier: retries reuse it, so the reference never changes.
        builder.HasIndex(s => new { s.TenantId, s.OrderId, s.Provider }).IsUnique();

        // Payout matching looks parcels up by the courier's consignment ID.
        builder.HasIndex(s => new { s.TenantId, s.Provider, s.ConsignmentId });

        // At most one live booking per order, whatever the courier: two parallel requests (or two
        // couriers) cannot both have a parcel in flight for the same order.
        builder.HasIndex(s => new { s.TenantId, s.OrderId })
            .IsUnique()
            .HasFilter("state IN ('Requested', 'Booked')")
            .HasDatabaseName("ux_shipments_one_live_per_order");

        // The status sync's work list: small (only parcels on the road), so the scan stays cheap
        // as delivered parcels pile up over the years.
        builder.HasIndex(s => new { s.TenantId, s.Provider, s.StatusCheckedAt })
            .HasFilter("state = 'Booked' AND (parcel_status IS NULL OR (parcel_status <> 'Delivered' AND parcel_status <> 'Returned' AND parcel_status <> 'Cancelled'))")
            .HasDatabaseName("ix_shipments_tracking");
    }
}
