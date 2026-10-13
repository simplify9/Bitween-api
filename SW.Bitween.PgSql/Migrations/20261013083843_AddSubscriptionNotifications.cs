using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "channel_id",
                schema: "infolink",
                table: "xchange_notification",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notifications",
                schema: "infolink",
                table: "subscription",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_xchange_notification_channel_id_finished_on",
                schema: "infolink",
                table: "xchange_notification",
                columns: new[] { "channel_id", "finished_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_xchange_notification_channel_id_finished_on",
                schema: "infolink",
                table: "xchange_notification");

            migrationBuilder.DropColumn(
                name: "channel_id",
                schema: "infolink",
                table: "xchange_notification");

            migrationBuilder.DropColumn(
                name: "notifications",
                schema: "infolink",
                table: "subscription");
        }
    }
}
