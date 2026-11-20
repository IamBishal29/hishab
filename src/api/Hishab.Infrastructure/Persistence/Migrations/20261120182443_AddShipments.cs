using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hishab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShipments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shipments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    merchant_reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    consignment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    tracking_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    delivery_fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    booked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipments", x => x.id);
                    table.ForeignKey(
                        name: "fk_shipments_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_order_id",
                table: "shipments",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id",
                table: "shipments",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_order_id_provider",
                table: "shipments",
                columns: new[] { "tenant_id", "order_id", "provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_shipments_one_live_per_order",
                table: "shipments",
                columns: new[] { "tenant_id", "order_id" },
                unique: true,
                filter: "state IN ('Requested', 'Booked')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shipments");
        }
    }
}
