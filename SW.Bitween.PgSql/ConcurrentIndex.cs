using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Bitween.PgSql;

/// <summary>
/// Builds an index on a table exchanges write to without stopping those writes.
/// </summary>
/// <remarks>
/// A plain CREATE INDEX holds a lock that blocks every insert for the whole build, minutes on a
/// table of tens of millions of rows, inside the startup migration. CONCURRENTLY doesn't, but it
/// cannot run in a transaction, and a build that fails part-way leaves the index behind marked
/// invalid: the IF NOT EXISTS would then skip the rebuild and the migration would pass with an
/// index Postgres never uses. So an invalid leftover goes first.
///
/// Not with a plain DROP INDEX, which locks the whole table: queued behind whatever is reading it,
/// it holds up every read and insert that comes after. The leftover is renamed out of the way
/// instead - a lock reads and writes don't wait for - and dropped concurrently, which a DO block
/// can't do. A valid index of the name is left as it is.
/// </remarks>
internal static class ConcurrentIndex
{
    /// <param name="definition">What follows <c>ON infolink.table</c>: the columns, and any WHERE.</param>
    public static void Create(MigrationBuilder migrationBuilder, string table, string name, string definition)
    {
        var leftover = name + "_invalid";

        migrationBuilder.Sql(
            $@"DO $$
               BEGIN
                   IF EXISTS (
                       SELECT 1
                       FROM pg_index i
                       JOIN pg_class c ON c.oid = i.indexrelid
                       JOIN pg_namespace n ON n.oid = c.relnamespace
                       WHERE n.nspname = '{BitweenDbContext.Schema}' AND c.relname = '{name}' AND NOT i.indisvalid
                   ) THEN
                       ALTER INDEX {BitweenDbContext.Schema}.{name} RENAME TO {leftover};
                   END IF;
               END $$;");

        Drop(migrationBuilder, leftover);

        migrationBuilder.Sql(
            $"CREATE INDEX CONCURRENTLY IF NOT EXISTS {name} ON {BitweenDbContext.Schema}.{table} {definition};",
            suppressTransaction: true);
    }

    public static void Drop(MigrationBuilder migrationBuilder, string name) =>
        migrationBuilder.Sql($"DROP INDEX CONCURRENTLY IF EXISTS {BitweenDbContext.Schema}.{name};",
            suppressTransaction: true);
}
