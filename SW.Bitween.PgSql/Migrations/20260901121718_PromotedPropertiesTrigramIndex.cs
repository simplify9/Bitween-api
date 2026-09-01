using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <summary>
    /// Makes the exchanges page's promoted-property search indexable.
    ///
    /// That search is a substring match, which reaches SQL as
    /// <c>LOWER(properties_raw) LIKE '%term%'</c>. A leading wildcard has no fixed prefix to look
    /// up, so the b-tree already on that column can never serve it and every search reads the whole
    /// table. Measured on 1,000,000 exchanges: searching a promoted key took 6.2s end to end, of
    /// which 4.8s was the rows query alone. With this index the same search is 228ms — the rows
    /// query drops to 4ms and what remains is the footer's count.
    ///
    /// A trigram (pg_trgm) GIN index indexes three-character sequences, which is what lets a
    /// wildcard-on-both-sides LIKE use an index at all. It costs roughly 20% of the table's size
    /// (83MB against 411MB on the million-row copy) and is written on every new exchange, which is
    /// the trade being made for a search that is otherwise unusable at this volume.
    ///
    /// Raw SQL rather than <c>HasIndex</c> because both pieces are Postgres-only, so this migration
    /// exists in the PgSql provider alone and leaves the shared model untouched.
    ///
    /// Built CONCURRENTLY: a plain CREATE INDEX holds an ACCESS EXCLUSIVE lock for the whole build
    /// (~6s per million rows locally, longer on a managed instance), and every exchange the engine
    /// processes writes a row to this table — so a plain build would stall processing for the
    /// duration. CONCURRENTLY cannot run inside a transaction, hence suppressTransaction.
    /// </summary>
    public partial class PromotedPropertiesTrigramIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            // IF NOT EXISTS so a previous CONCURRENTLY build that failed part-way (which leaves an
            // INVALID index behind rather than nothing) doesn't turn a retry into a hard error.
            migrationBuilder.Sql(
                @"CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_xchange_promoted_properties_properties_raw_trgm
                      ON infolink.xchange_promoted_properties
                      USING gin (lower(properties_raw) gin_trgm_ops);",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS infolink.ix_xchange_promoted_properties_properties_raw_trgm;",
                suppressTransaction: true);

            // pg_trgm is left installed: dropping it would break anything else that came to depend
            // on it, and an unused extension costs nothing.
        }
    }
}
