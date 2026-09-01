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
    /// Raw SQL rather than <c>HasIndex</c> because the index is on an <em>expression</em>,
    /// <c>lower(properties_raw)</c>, and EF can only model indexes over plain columns. The
    /// model-declarable alternative — a trigram index on the bare column, matched with
    /// <c>ILIKE</c> — was measured and does work (98ms, index scan), but it needs
    /// <c>EF.Functions.ILike</c> at the call site, and <c>Xchanges/Search.cs</c> lives in
    /// SW.Bitween.Api, which deliberately references only EntityFrameworkCore.Relational and no
    /// provider. Keeping the Postgres-specific part inside a Postgres-only migration is the lesser
    /// evil; the search code stays provider-agnostic and unchanged.
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

            // A CONCURRENTLY build that fails part-way leaves the index behind marked INVALID
            // rather than removing it. Postgres ignores an invalid index when planning, but it
            // still occupies the name — so the IF NOT EXISTS below would skip the rebuild, the
            // migration would be recorded as applied, and the search would quietly keep scanning
            // the whole table while the deploy looked green. Clear that state first.
            //
            // A plain DROP, not CONCURRENTLY: the latter cannot run inside a DO block, and an
            // invalid index holds no data, so the drop is quick. The brief exclusive lock only
            // happens on a retry after a failed build, never on a first run.
            migrationBuilder.Sql(
                @"DO $$
                  BEGIN
                      IF EXISTS (
                          SELECT 1
                          FROM pg_index i
                          JOIN pg_class c ON c.oid = i.indexrelid
                          JOIN pg_namespace n ON n.oid = c.relnamespace
                          WHERE n.nspname = 'infolink'
                            AND c.relname = 'ix_xchange_promoted_properties_properties_raw_trgm'
                            AND NOT i.indisvalid
                      ) THEN
                          DROP INDEX infolink.ix_xchange_promoted_properties_properties_raw_trgm;
                      END IF;
                  END $$;");

            migrationBuilder.Sql(
                @"CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_xchange_promoted_properties_properties_raw_trgm
                      ON infolink.xchange_promoted_properties
                      USING gin (lower(properties_raw) gin_trgm_ops);",
                suppressTransaction: true);

            // Not optional. Postgres only gathers statistics for an *expression* index when ANALYZE
            // runs, so until it does, the planner has no idea how selective
            // lower(properties_raw) LIKE ... is and can pick a far worse plan than it would with no
            // index at all. Measured on the million-row copy: 257ms with statistics, 19,437ms
            // without — slower than the 8,103ms it takes with no index whatsoever. Autovacuum would
            // eventually analyze the table on its own; this stops the window between deploying and
            // that happening from being a regression.
            migrationBuilder.Sql("ANALYZE infolink.xchange_promoted_properties;");
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
