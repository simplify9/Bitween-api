using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AddApiGateway : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:hstore", ",,");

            migrationBuilder.AddColumn<Dictionary<string, string>>(
                name: "additional_values",
                schema: "infolink",
                table: "partner",
                type: "hstore",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "api_gateway",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    subscription_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_gateway", x => x.id);
                    table.ForeignKey(
                        name: "fk_api_gateway_subscription_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "infolink",
                        principalTable: "subscription",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "api_gateway_partner",
                schema: "infolink",
                columns: table => new
                {
                    api_gateway_id = table.Column<int>(type: "integer", nullable: false),
                    partner_id = table.Column<int>(type: "integer", nullable: false),
                    subscription_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_gateway_partner", x => new { x.api_gateway_id, x.partner_id, x.subscription_id });
                    table.ForeignKey(
                        name: "fk_api_gateway_partner_api_gateway_api_gateway_id",
                        column: x => x.api_gateway_id,
                        principalSchema: "infolink",
                        principalTable: "api_gateway",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_api_gateway_partner_partner_partner_id",
                        column: x => x.partner_id,
                        principalSchema: "infolink",
                        principalTable: "partner",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_api_gateway_partner_subscription_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "infolink",
                        principalTable: "subscription",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "partner",
                keyColumn: "id",
                keyValue: 1,
                column: "additional_values",
                value: null);

            migrationBuilder.CreateIndex(
                name: "ix_api_gateway_subscription_id",
                schema: "infolink",
                table: "api_gateway",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_gateway_partner_partner_id",
                schema: "infolink",
                table: "api_gateway_partner",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_api_gateway_partner_subscription_id",
                schema: "infolink",
                table: "api_gateway_partner",
                column: "subscription_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_gateway_partner",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "api_gateway",
                schema: "infolink");

            migrationBuilder.DropColumn(
                name: "additional_values",
                schema: "infolink",
                table: "partner");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:hstore", ",,");
        }
    }
}
