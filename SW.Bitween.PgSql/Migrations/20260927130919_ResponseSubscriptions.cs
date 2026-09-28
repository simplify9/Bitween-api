using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class ResponseSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "run_on_bad_responses",
                schema: "infolink",
                table: "subscription",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                schema: "infolink",
                table: "on_hold_xchange",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "partner_id",
                schema: "infolink",
                table: "on_hold_xchange",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "run_on_bad_responses",
                schema: "infolink",
                table: "subscription");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                schema: "infolink",
                table: "on_hold_xchange");

            migrationBuilder.DropColumn(
                name: "partner_id",
                schema: "infolink",
                table: "on_hold_xchange");
        }
    }
}
