using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class ApiKeyUses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_key_use",
                schema: "infolink",
                columns: table => new
                {
                    partner_id = table.Column<int>(type: "integer", nullable: false),
                    key_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    last_used_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_key_use", x => new { x.partner_id, x.key_name });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_key_use",
                schema: "infolink");
        }
    }
}
