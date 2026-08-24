using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MySql.Migrations
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
                    GroupId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AttemptsUsed = table.Column<int>(type: "int", nullable: false),
                    LastAttemptOn = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetryGroupUsages", x => new { x.SubscriptionId, x.GroupId });
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RetryGroupUsages");

            migrationBuilder.AddColumn<string>(
                name: "GroupAttemptCounts",
                table: "Xchanges",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "GroupAttemptCounts",
                table: "DelayedRetries",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
