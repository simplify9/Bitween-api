using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace SW.Bitween.PgSql.Migrations
{
    public partial class update4 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "paused_on",
                schema: "Bitween",
                table: "subscription",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "on_hold_xchange",
                schema: "Bitween",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    subscription_id = table.Column<int>(nullable: false),
                    references = table.Column<string[]>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_on_hold_xchange", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_on_hold_xchange_subscription_id",
                schema: "Bitween",
                table: "on_hold_xchange",
                column: "subscription_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "on_hold_xchange",
                schema: "Bitween");

            migrationBuilder.DropColumn(
                name: "paused_on",
                schema: "Bitween",
                table: "subscription");
        }
    }
}
