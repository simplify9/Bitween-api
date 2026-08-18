using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class RetryBudgetAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempt_number",
                schema: "infolink",
                table: "xchange_result",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "retry_group_id",
                schema: "infolink",
                table: "xchange_result",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "notifier_id",
                schema: "infolink",
                table: "xchange_notification",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "alert_handler_id",
                schema: "infolink",
                table: "retry_policy",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "alert_handler_properties",
                schema: "infolink",
                table: "retry_policy",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "exhausted_notified_on",
                schema: "infolink",
                table: "retry_group_usage",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "retry_alert_override",
                schema: "infolink",
                columns: table => new
                {
                    subscription_id = table.Column<int>(type: "integer", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_mode = table.Column<byte>(type: "smallint", nullable: false),
                    alert_handler_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    alert_handler_properties = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retry_alert_override", x => new { x.subscription_id, x.group_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_xchange_result_retry_group_id",
                schema: "infolink",
                table: "xchange_result",
                column: "retry_group_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "retry_alert_override",
                schema: "infolink");

            migrationBuilder.DropIndex(
                name: "ix_xchange_result_retry_group_id",
                schema: "infolink",
                table: "xchange_result");

            migrationBuilder.DropColumn(
                name: "attempt_number",
                schema: "infolink",
                table: "xchange_result");

            migrationBuilder.DropColumn(
                name: "retry_group_id",
                schema: "infolink",
                table: "xchange_result");

            migrationBuilder.DropColumn(
                name: "alert_handler_id",
                schema: "infolink",
                table: "retry_policy");

            migrationBuilder.DropColumn(
                name: "alert_handler_properties",
                schema: "infolink",
                table: "retry_policy");

            migrationBuilder.DropColumn(
                name: "exhausted_notified_on",
                schema: "infolink",
                table: "retry_group_usage");

            migrationBuilder.AlterColumn<int>(
                name: "notifier_id",
                schema: "infolink",
                table: "xchange_notification",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
