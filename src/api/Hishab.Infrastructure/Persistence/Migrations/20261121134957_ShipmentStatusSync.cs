using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hishab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShipmentStatusSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "courier_status",
                table: "shipments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parcel_status",
                table: "shipments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "parcel_status_at",
                table: "shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "status_checked_at",
                table: "shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tracking",
                table: "shipments",
                columns: new[] { "tenant_id", "provider", "status_checked_at" },
                filter: "state = 'Booked' AND (parcel_status IS NULL OR (parcel_status <> 'Delivered' AND parcel_status <> 'Returned' AND parcel_status <> 'Cancelled'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_shipments_tracking",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "courier_status",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "parcel_status",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "parcel_status_at",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "status_checked_at",
                table: "shipments");
        }
    }
}
