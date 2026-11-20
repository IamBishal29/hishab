using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hishab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCourierAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "courier_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    protected_credentials = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    credential_hint = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_courier_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_courier_accounts_shops_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_courier_accounts_tenant_id",
                table: "courier_accounts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_courier_accounts_tenant_id_provider",
                table: "courier_accounts",
                columns: new[] { "tenant_id", "provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "courier_accounts");

            migrationBuilder.DropTable(
                name: "data_protection_keys");
        }
    }
}
