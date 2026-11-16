using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MsSql.Migrations
{
    /// <inheritdoc />
    public partial class AddGatewaySignatureAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SignatureHeader",
                table: "ApiGateways",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SignatureScheme",
                table: "ApiGateways",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SignatureSecretProperty",
                table: "ApiGateways",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SignatureToleranceSeconds",
                table: "ApiGateways",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignatureHeader",
                table: "ApiGateways");

            migrationBuilder.DropColumn(
                name: "SignatureScheme",
                table: "ApiGateways");

            migrationBuilder.DropColumn(
                name: "SignatureSecretProperty",
                table: "ApiGateways");

            migrationBuilder.DropColumn(
                name: "SignatureToleranceSeconds",
                table: "ApiGateways");
        }
    }
}
