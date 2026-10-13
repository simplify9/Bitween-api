using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MsSql.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChannelId",
                table: "XchangeNotifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notifications",
                table: "Subscriptions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_XchangeNotifications_ChannelId_FinishedOn",
                table: "XchangeNotifications",
                columns: new[] { "ChannelId", "FinishedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_XchangeNotifications_ChannelId_FinishedOn",
                table: "XchangeNotifications");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                table: "XchangeNotifications");

            migrationBuilder.DropColumn(
                name: "Notifications",
                table: "Subscriptions");
        }
    }
}
