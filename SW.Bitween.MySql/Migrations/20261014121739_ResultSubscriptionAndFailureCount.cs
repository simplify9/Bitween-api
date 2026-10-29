using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MySql.Migrations
{
    /// <summary>
    /// Each result names its subscription and how many failures in a row it makes, so auto-pause
    /// counts from the results rather than from a counter every exchange updated on the
    /// subscription row (finding 4i of docs/design/processing-performance.md). Nullable, so results
    /// from before stay as they are. Secondary indexes build online on InnoDB.
    /// </summary>
    public partial class ResultSubscriptionAndFailureCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailures",
                table: "XchangeResults",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubscriptionId",
                table: "XchangeResults",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_XchangeResults_SubscriptionId_FinishedOn",
                table: "XchangeResults",
                columns: new[] { "SubscriptionId", "FinishedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_XchangeResults_SubscriptionId_FinishedOn",
                table: "XchangeResults");

            migrationBuilder.DropColumn(
                name: "ConsecutiveFailures",
                table: "XchangeResults");

            migrationBuilder.DropColumn(
                name: "SubscriptionId",
                table: "XchangeResults");
        }
    }
}
