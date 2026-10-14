using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <summary>
    /// Each result names its subscription and how many failures in a row it makes, so auto-pause
    /// counts from the results rather than from a counter every exchange updated on the
    /// subscription row (finding 4i of docs/design/processing-performance.md). Nullable, so results
    /// from before stay as they are. The index is built without stopping writes to the table.
    /// </summary>
    public partial class ResultSubscriptionAndFailureCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "consecutive_failures",
                schema: "infolink",
                table: "xchange_result",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "subscription_id",
                schema: "infolink",
                table: "xchange_result",
                type: "integer",
                nullable: true);

            // Outside the transaction, on what can be the largest table there is: see ConcurrentIndex.
            ConcurrentIndex.Create(migrationBuilder, "xchange_result", "ix_xchange_result_subscription_id_finished_on",
                "(subscription_id, finished_on)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ConcurrentIndex.Drop(migrationBuilder, "ix_xchange_result_subscription_id_finished_on");

            migrationBuilder.DropColumn(
                name: "consecutive_failures",
                schema: "infolink",
                table: "xchange_result");

            migrationBuilder.DropColumn(
                name: "subscription_id",
                schema: "infolink",
                table: "xchange_result");
        }
    }
}
