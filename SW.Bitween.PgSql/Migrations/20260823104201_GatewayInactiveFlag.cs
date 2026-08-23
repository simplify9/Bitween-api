using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class GatewayInactiveFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "inactive",
                schema: "infolink",
                table: "bus_gateway",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "inactive",
                schema: "infolink",
                table: "api_gateway",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "inactive",
                schema: "infolink",
                table: "bus_gateway");

            migrationBuilder.DropColumn(
                name: "inactive",
                schema: "infolink",
                table: "api_gateway");
        }
    }
}
