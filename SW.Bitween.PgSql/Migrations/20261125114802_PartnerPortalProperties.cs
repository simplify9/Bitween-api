using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class PartnerPortalProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Dictionary<string, string>>(
                name: "portal_access",
                schema: "infolink",
                table: "partner",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "partner_property_change",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    partner_id = table.Column<int>(type: "integer", nullable: false),
                    values = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<byte>(type: "smallint", nullable: false),
                    requested_by = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    requested_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    decided_by = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    decided_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_property_change", x => x.id);
                    table.ForeignKey(
                        name: "fk_partner_property_change_partner_partner_id",
                        column: x => x.partner_id,
                        principalSchema: "infolink",
                        principalTable: "partner",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "partner",
                keyColumn: "id",
                keyValue: 1,
                column: "portal_access",
                value: null);

            migrationBuilder.CreateIndex(
                name: "ix_partner_property_change_partner_id",
                schema: "infolink",
                table: "partner_property_change",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_partner_property_change_status",
                schema: "infolink",
                table: "partner_property_change",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "partner_property_change",
                schema: "infolink");

            migrationBuilder.DropColumn(
                name: "portal_access",
                schema: "infolink",
                table: "partner");
        }
    }
}
