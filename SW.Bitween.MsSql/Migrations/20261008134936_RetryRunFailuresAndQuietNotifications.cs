using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MsSql.Migrations
{
    /// <inheritdoc />
    public partial class RetryRunFailuresAndQuietNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Suppressed",
                table: "XchangeNotifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RunFailures",
                table: "DelayedRetries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_XchangeNotifications_NotifierId_FinishedOn",
                table: "XchangeNotifications",
                columns: new[] { "NotifierId", "FinishedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_XchangeNotifications_NotifierId_FinishedOn",
                table: "XchangeNotifications");

            migrationBuilder.DropColumn(
                name: "Suppressed",
                table: "XchangeNotifications");

            migrationBuilder.DropColumn(
                name: "RunFailures",
                table: "DelayedRetries");
        }
    }
}
