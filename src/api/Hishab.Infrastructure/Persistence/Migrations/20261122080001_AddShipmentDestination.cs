using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hishab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentDestination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "destination_area_id",
                table: "shipments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destination_city_id",
                table: "shipments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destination_zone_id",
                table: "shipments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_shipments_destination_complete",
                table: "shipments",
                sql: "(destination_city_id IS NULL) = (destination_zone_id IS NULL) AND (destination_area_id IS NULL OR destination_zone_id IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_shipments_destination_complete",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "destination_area_id",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "destination_city_id",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "destination_zone_id",
                table: "shipments");
        }
    }
}
