using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AddGatewayJwtAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "login_identity",
                schema: "infolink",
                table: "partner",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "auth_method",
                schema: "infolink",
                table: "api_gateway",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "jwt_audience",
                schema: "infolink",
                table: "api_gateway",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "jwt_issuer",
                schema: "infolink",
                table: "api_gateway",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "jwt_partner_claim",
                schema: "infolink",
                table: "api_gateway",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "partner",
                keyColumn: "id",
                keyValue: 1,
                column: "login_identity",
                value: null);

            migrationBuilder.CreateIndex(
                name: "ix_partner_login_identity",
                schema: "infolink",
                table: "partner",
                column: "login_identity",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_partner_login_identity",
                schema: "infolink",
                table: "partner");

            migrationBuilder.DropColumn(
                name: "login_identity",
                schema: "infolink",
                table: "partner");

            migrationBuilder.DropColumn(
                name: "auth_method",
                schema: "infolink",
                table: "api_gateway");

            migrationBuilder.DropColumn(
                name: "jwt_audience",
                schema: "infolink",
                table: "api_gateway");

            migrationBuilder.DropColumn(
                name: "jwt_issuer",
                schema: "infolink",
                table: "api_gateway");

            migrationBuilder.DropColumn(
                name: "jwt_partner_claim",
                schema: "infolink",
                table: "api_gateway");
        }
    }
}
