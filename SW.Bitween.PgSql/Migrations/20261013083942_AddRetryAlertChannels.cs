using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AddRetryAlertChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "alert_channel_id",
                schema: "infolink",
                table: "retry_policy",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "alert_channel_id",
                schema: "infolink",
                table: "retry_alert_override",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "alert_channel_id",
                schema: "infolink",
                table: "retry_policy");

            migrationBuilder.DropColumn(
                name: "alert_channel_id",
                schema: "infolink",
                table: "retry_alert_override");
        }
    }
}
