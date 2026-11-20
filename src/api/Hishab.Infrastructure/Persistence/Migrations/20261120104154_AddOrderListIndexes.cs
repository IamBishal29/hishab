using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hishab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderListIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_tenant_id_created_at",
                table: "orders");

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_created_at_id",
                table: "orders",
                columns: new[] { "tenant_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_status_created_at_id",
                table: "orders",
                columns: new[] { "tenant_id", "status", "created_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_tenant_id_created_at_id",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_tenant_id_status_created_at_id",
                table: "orders");

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_created_at",
                table: "orders",
                columns: new[] { "tenant_id", "created_at" });
        }
    }
}
