using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <summary>
    /// The results with a bad response, in an index of their own, so the Bad response tab counts them
    /// and the dashboard finds the latest without reading every result. They are a small part of the
    /// table, often none of it. Built without stopping writes to it.
    /// </summary>
    public partial class BadResponseIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Outside the transaction, on what can be the largest tables there are: see ConcurrentIndex.
            ConcurrentIndex.Create(migrationBuilder, "xchange_result", "ix_xchange_result_bad_response",
                "(id) WHERE response_bad");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ConcurrentIndex.Drop(migrationBuilder, "ix_xchange_result_bad_response");
        }
    }
}
