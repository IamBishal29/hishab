using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hishab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderNumbersAndIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "last_order_number",
                table: "shops",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "number",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    operation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    request_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_keys", x => new { x.tenant_id, x.key });
                });

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_number",
                table: "orders",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_tenant_id",
                table: "idempotency_keys",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_keys");

            migrationBuilder.DropIndex(
                name: "ix_orders_tenant_id_number",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "last_order_number",
                table: "shops");

            migrationBuilder.DropColumn(
                name: "number",
                table: "orders");
        }
    }
}
