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
            // IF NOT EXISTS: the columns commit before the index build below, which runs outside the
            // transaction. A build that fails part-way (a timeout, the pod restarted) leaves them
            // without the migration recorded, and the next start must not fail on them.
            migrationBuilder.Sql(
                "ALTER TABLE infolink.xchange_result ADD COLUMN IF NOT EXISTS consecutive_failures integer;");

            migrationBuilder.Sql(
                "ALTER TABLE infolink.xchange_result ADD COLUMN IF NOT EXISTS subscription_id integer;");

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
