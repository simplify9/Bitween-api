using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionAutoPause : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "auto_pause_after_failures",
                schema: "infolink",
                table: "subscription",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "paused_automatically",
                schema: "infolink",
                table: "subscription",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auto_pause_after_failures",
                schema: "infolink",
                table: "subscription");

            migrationBuilder.DropColumn(
                name: "paused_automatically",
                schema: "infolink",
                table: "subscription");
        }
    }
}
