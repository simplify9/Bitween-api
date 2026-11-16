using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AddGatewaySignatureAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "signature_header",
                schema: "infolink",
                table: "api_gateway",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "signature_scheme",
                schema: "infolink",
                table: "api_gateway",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "signature_secret_property",
                schema: "infolink",
                table: "api_gateway",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "signature_tolerance_seconds",
                schema: "infolink",
                table: "api_gateway",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "signature_header",
                schema: "infolink",
                table: "api_gateway");

            migrationBuilder.DropColumn(
                name: "signature_scheme",
                schema: "infolink",
                table: "api_gateway");

            migrationBuilder.DropColumn(
                name: "signature_secret_property",
                schema: "infolink",
                table: "api_gateway");

            migrationBuilder.DropColumn(
                name: "signature_tolerance_seconds",
                schema: "infolink",
                table: "api_gateway");
        }
    }
}
