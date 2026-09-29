using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddGatewayJwtAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LoginIdentity",
                table: "Partners",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "AuthMethod",
                table: "ApiGateways",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "JwtAudience",
                table: "ApiGateways",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "JwtIssuer",
                table: "ApiGateways",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "JwtPartnerClaim",
                table: "ApiGateways",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "Partners",
                keyColumn: "Id",
                keyValue: 1,
                column: "LoginIdentity",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_Partners_LoginIdentity",
                table: "Partners",
                column: "LoginIdentity",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Partners_LoginIdentity",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "LoginIdentity",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "AuthMethod",
                table: "ApiGateways");

            migrationBuilder.DropColumn(
                name: "JwtAudience",
                table: "ApiGateways");

            migrationBuilder.DropColumn(
                name: "JwtIssuer",
                table: "ApiGateways");

            migrationBuilder.DropColumn(
                name: "JwtPartnerClaim",
                table: "ApiGateways");
        }
    }
}
