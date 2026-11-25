using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class PartnerMappingChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "changes_need_approval",
                schema: "infolink",
                table: "partner",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "partner_mapping_change",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    subscription_id = table.Column<int>(type: "integer", nullable: false),
                    partner_id = table.Column<int>(type: "integer", nullable: false),
                    mapper_properties = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<byte>(type: "smallint", nullable: false),
                    requested_by = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    requested_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    decided_by = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    decided_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_mapping_change", x => x.id);
                    table.ForeignKey(
                        name: "fk_partner_mapping_change_partner_partner_id",
                        column: x => x.partner_id,
                        principalSchema: "infolink",
                        principalTable: "partner",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_partner_mapping_change_subscription_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "infolink",
                        principalTable: "subscription",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "partner",
                keyColumn: "id",
                keyValue: 1,
                column: "changes_need_approval",
                value: false);

            migrationBuilder.CreateIndex(
                name: "ix_partner_mapping_change_partner_id",
                schema: "infolink",
                table: "partner_mapping_change",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_partner_mapping_change_status",
                schema: "infolink",
                table: "partner_mapping_change",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_partner_mapping_change_subscription_id_partner_id",
                schema: "infolink",
                table: "partner_mapping_change",
                columns: new[] { "subscription_id", "partner_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "partner_mapping_change",
                schema: "infolink");

            migrationBuilder.DropColumn(
                name: "changes_need_approval",
                schema: "infolink",
                table: "partner");
        }
    }
}
