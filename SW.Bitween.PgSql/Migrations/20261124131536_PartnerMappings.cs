using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class PartnerMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "partner_mapping",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    subscription_id = table.Column<int>(type: "integer", nullable: false),
                    partner_id = table.Column<int>(type: "integer", nullable: false),
                    mapper_properties = table.Column<string>(type: "jsonb", nullable: true),
                    created_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    modified_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partner_mapping", x => x.id);
                    table.ForeignKey(
                        name: "fk_partner_mapping_partner_partner_id",
                        column: x => x.partner_id,
                        principalSchema: "infolink",
                        principalTable: "partner",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_partner_mapping_subscription_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "infolink",
                        principalTable: "subscription",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_partner_mapping_partner_id",
                schema: "infolink",
                table: "partner_mapping",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_partner_mapping_subscription_id_partner_id",
                schema: "infolink",
                table: "partner_mapping",
                columns: new[] { "subscription_id", "partner_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "partner_mapping",
                schema: "infolink");
        }
    }
}
