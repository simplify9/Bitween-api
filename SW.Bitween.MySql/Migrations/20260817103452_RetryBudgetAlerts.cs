using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MySql.Migrations
{
    /// <inheritdoc />
    public partial class RetryBudgetAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptNumber",
                table: "XchangeResults",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RetryGroupId",
                table: "XchangeResults",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AlterColumn<int>(
                name: "NotifierId",
                table: "XchangeNotifications",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "AlertHandlerId",
                table: "RetryPolicies",
                type: "varchar(200)",
                unicode: false,
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AlertHandlerProperties",
                table: "RetryPolicies",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExhaustedNotifiedOn",
                table: "RetryGroupUsages",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RetryAlertOverrides",
                columns: table => new
                {
                    SubscriptionId = table.Column<int>(type: "int", nullable: false),
                    GroupId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AlertMode = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    AlertHandlerId = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AlertHandlerProperties = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetryAlertOverrides", x => new { x.SubscriptionId, x.GroupId });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_XchangeResults_RetryGroupId",
                table: "XchangeResults",
                column: "RetryGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RetryAlertOverrides");

            migrationBuilder.DropIndex(
                name: "IX_XchangeResults_RetryGroupId",
                table: "XchangeResults");

            migrationBuilder.DropColumn(
                name: "AttemptNumber",
                table: "XchangeResults");

            migrationBuilder.DropColumn(
                name: "RetryGroupId",
                table: "XchangeResults");

            migrationBuilder.DropColumn(
                name: "AlertHandlerId",
                table: "RetryPolicies");

            migrationBuilder.DropColumn(
                name: "AlertHandlerProperties",
                table: "RetryPolicies");

            migrationBuilder.DropColumn(
                name: "ExhaustedNotifiedOn",
                table: "RetryGroupUsages");

            // These rows are the alert's own delivery log, and they are the reason the column was
            // made nullable. Rolling the feature back leaves nowhere to put them, and the column
            // cannot go back to NOT NULL while they are here, so they go with the feature.
            migrationBuilder.Sql(
                "DELETE FROM `XchangeNotifications` WHERE `NotifierId` IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "NotifierId",
                table: "XchangeNotifications",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
