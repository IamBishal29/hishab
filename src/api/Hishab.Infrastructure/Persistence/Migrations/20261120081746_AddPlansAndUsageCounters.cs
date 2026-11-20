using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hishab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlansAndUsageCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "plan",
                table: "shops",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Free"); // existing shops start on Free, like new ones

            migrationBuilder.CreateTable(
                name: "usage_counters",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: false),
                    metric = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_counters", x => new { x.tenant_id, x.period, x.metric });
                    table.CheckConstraint("ck_usage_counters_count_non_negative", "count >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_usage_counters_tenant_id",
                table: "usage_counters",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "usage_counters");

            migrationBuilder.DropColumn(
                name: "plan",
                table: "shops");
        }
    }
}
