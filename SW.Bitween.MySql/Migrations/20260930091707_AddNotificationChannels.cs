using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationChannels : Migration
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
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "AlertChannelId",
                table: "RetryPolicies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AlertChannelId",
                table: "RetryAlertOverrides",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NotificationChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HandlerId = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HandlerProperties = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedOn = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ModifiedOn = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationChannels", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_XchangeNotifications_ChannelId_FinishedOn",
                table: "XchangeNotifications",
                columns: new[] { "ChannelId", "FinishedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationChannels_Name",
                table: "NotificationChannels",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationChannels");

            migrationBuilder.DropIndex(
                name: "IX_XchangeNotifications_ChannelId_FinishedOn",
                table: "XchangeNotifications");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                table: "XchangeNotifications");

            migrationBuilder.DropColumn(
                name: "Notifications",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "AlertChannelId",
                table: "RetryPolicies");

            migrationBuilder.DropColumn(
                name: "AlertChannelId",
                table: "RetryAlertOverrides");
        }
    }
}
