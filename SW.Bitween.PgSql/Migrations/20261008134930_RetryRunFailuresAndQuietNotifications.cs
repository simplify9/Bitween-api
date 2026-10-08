using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class RetryRunFailuresAndQuietNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "suppressed",
                schema: "infolink",
                table: "xchange_notification",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "run_failures",
                schema: "infolink",
                table: "delayed_retry",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_xchange_notification_notifier_id_finished_on",
                schema: "infolink",
                table: "xchange_notification",
                columns: new[] { "notifier_id", "finished_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_xchange_notification_notifier_id_finished_on",
                schema: "infolink",
                table: "xchange_notification");

            migrationBuilder.DropColumn(
                name: "suppressed",
                schema: "infolink",
                table: "xchange_notification");

            migrationBuilder.DropColumn(
                name: "run_failures",
                schema: "infolink",
                table: "delayed_retry");
        }
    }
}
