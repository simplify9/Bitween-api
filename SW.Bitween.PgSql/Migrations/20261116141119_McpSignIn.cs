using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class McpSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "client_id",
                schema: "infolink",
                table: "RefreshTokens",
                type: "character varying(2048)",
                unicode: false,
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "client_name",
                schema: "infolink",
                table: "RefreshTokens",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "via",
                schema: "infolink",
                table: "AuditEntries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SignInCodeUses",
                schema: "infolink",
                columns: table => new
                {
                    nonce = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: false),
                    expires_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sign_in_code_uses", x => x.nonce);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignInCodeUses",
                schema: "infolink");

            migrationBuilder.DropColumn(
                name: "client_id",
                schema: "infolink",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "client_name",
                schema: "infolink",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "via",
                schema: "infolink",
                table: "AuditEntries");
        }
    }
}
