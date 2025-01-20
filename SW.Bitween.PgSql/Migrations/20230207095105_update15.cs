using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    public partial class update15 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_trail",
                schema: "Bitween",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    document_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<int>(type: "integer", nullable: false),
                    state_before = table.Column<string>(type: "text", nullable: true),
                    state_after = table.Column<string>(type: "text", nullable: true),
                    created_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_trail", x => x.id);
                    table.ForeignKey(
                        name: "fk_document_trail_document_document_id",
                        column: x => x.document_id,
                        principalSchema: "Bitween",
                        principalTable: "document",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscription_trail",
                schema: "Bitween",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    subscription_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<int>(type: "integer", nullable: false),
                    state_before = table.Column<string>(type: "text", nullable: true),
                    state_after = table.Column<string>(type: "text", nullable: true),
                    created_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_trail", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscription_trail_subscription_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "Bitween",
                        principalTable: "subscription",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                schema: "Bitween",
                table: "Accounts",
                keyColumn: "id",
                keyValue: 9999,
                column: "created_on",
                value: new DateTime(2021, 12, 31, 22, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.CreateIndex(
                name: "ix_document_trail_created_on",
                schema: "Bitween",
                table: "document_trail",
                column: "created_on");

            migrationBuilder.CreateIndex(
                name: "ix_document_trail_document_id",
                schema: "Bitween",
                table: "document_trail",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_trail_created_on",
                schema: "Bitween",
                table: "subscription_trail",
                column: "created_on");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_trail_subscription_id",
                schema: "Bitween",
                table: "subscription_trail",
                column: "subscription_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_trail",
                schema: "Bitween");

            migrationBuilder.DropTable(
                name: "subscription_trail",
                schema: "Bitween");

            migrationBuilder.UpdateData(
                schema: "Bitween",
                table: "Accounts",
                keyColumn: "id",
                keyValue: 9999,
                column: "created_on",
                value: new DateTime(2021, 12, 31, 21, 0, 0, 0, DateTimeKind.Utc));
        }
    }
}
