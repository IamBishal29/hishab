using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hishab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payouts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    net_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payouts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payout_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    consignment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    collected = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    delivery_fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    cod_fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    match = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cod_due = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    settled_shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payout_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payout_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_payout_lines_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payout_lines_payouts_payout_id",
                        column: x => x.payout_id,
                        principalTable: "payouts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_payout_lines_shipments_settled_shipment_id",
                        column: x => x.settled_shipment_id,
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_provider_consignment_id",
                table: "shipments",
                columns: new[] { "tenant_id", "provider", "consignment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payout_lines_order_id",
                table: "payout_lines",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_payout_lines_payout_id_sequence",
                table: "payout_lines",
                columns: new[] { "payout_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payout_lines_settled_shipment",
                table: "payout_lines",
                column: "settled_shipment_id",
                unique: true,
                filter: "settled_shipment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payouts_tenant_id",
                table: "payouts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payouts_tenant_id_paid_on_recorded_at",
                table: "payouts",
                columns: new[] { "tenant_id", "paid_on", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ux_payouts_provider_reference",
                table: "payouts",
                columns: new[] { "tenant_id", "provider", "reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payout_lines");

            migrationBuilder.DropTable(
                name: "payouts");

            migrationBuilder.DropIndex(
                name: "ix_shipments_tenant_id_provider_consignment_id",
                table: "shipments");
        }
    }
}
