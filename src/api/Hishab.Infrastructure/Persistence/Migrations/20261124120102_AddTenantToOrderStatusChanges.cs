using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hishab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantToOrderStatusChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "order_status_changes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Existing history rows take their order's tenant; new rows are stamped by SaveChanges.
            migrationBuilder.Sql("""
                UPDATE order_status_changes h
                SET tenant_id = o.tenant_id
                FROM orders o
                WHERE o.id = h.order_id
                """);

            migrationBuilder.CreateIndex(
                name: "ix_order_status_changes_tenant_id_to_changed_at",
                table: "order_status_changes",
                columns: new[] { "tenant_id", "to", "changed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_order_status_changes_tenant_id_to_changed_at",
                table: "order_status_changes");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "order_status_changes");
        }
    }
}
