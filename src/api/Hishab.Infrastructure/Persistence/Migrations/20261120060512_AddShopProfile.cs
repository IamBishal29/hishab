using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hishab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShopProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "default_courier",
                table: "shops",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "delivery_charge_inside_dhaka",
                table: "shops",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 60m); // backfill existing shops with the same defaults as new ones (Shop.DefaultChargeInsideDhaka)

            migrationBuilder.AddColumn<decimal>(
                name: "delivery_charge_outside_dhaka",
                table: "shops",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 120m); // Shop.DefaultChargeOutsideDhaka

            migrationBuilder.AddColumn<string>(
                name: "logo_url",
                table: "shops",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pickup_address",
                table: "shops",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_courier",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "delivery_charge_inside_dhaka",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "delivery_charge_outside_dhaka",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "logo_url",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "pickup_address",
                table: "shops");
        }
    }
}
