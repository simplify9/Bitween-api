using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <summary>
    /// The failed results and the retries, each in an index of its own, so the dashboard counts the
    /// failures still to act on and finds the chains that keep failing without walking every
    /// exchange. Both are a few percent of their table. Built without stopping writes to it.
    /// </summary>
    public partial class DashboardIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Outside the transaction, on what can be the largest tables there are: see ConcurrentIndex.
            ConcurrentIndex.Create(migrationBuilder, "xchange_result", "ix_xchange_result_failed",
                "(id) WHERE NOT success");

            ConcurrentIndex.Create(migrationBuilder, "xchange", "ix_xchange_retries_started_on",
                "(started_on) INCLUDE (id, retry_for) WHERE retry_for IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ConcurrentIndex.Drop(migrationBuilder, "ix_xchange_result_failed");

            ConcurrentIndex.Drop(migrationBuilder, "ix_xchange_retries_started_on");
        }
    }
}
