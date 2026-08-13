using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MsSql.Migrations
{
    /// <inheritdoc />
    public partial class SharedRetryGroupTotals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GroupAttemptCounts",
                table: "Xchanges");

            migrationBuilder.DropColumn(
                name: "GroupAttemptCounts",
                table: "DelayedRetries");

            migrationBuilder.CreateTable(
                name: "RetryGroupUsages",
                columns: table => new
                {
                    SubscriptionId = table.Column<int>(type: "int", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptsUsed = table.Column<int>(type: "int", nullable: false),
                    LastAttemptOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetryGroupUsages", x => new { x.SubscriptionId, x.GroupId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RetryGroupUsages");

            migrationBuilder.AddColumn<string>(
                name: "GroupAttemptCounts",
                table: "Xchanges",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GroupAttemptCounts",
                table: "DelayedRetries",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
