using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using SW.Bitween.Domain;

namespace SW.Bitween.Services.XchangeStates;

/// <summary>
/// The index the Exchanges page and dashboard read <see cref="Xchange.Status"/> and
/// <see cref="Xchange.Retried"/> through, built by <see cref="XchangeStateFiller"/> and never by a
/// migration, so no deploy waits for it on a big table.
/// </summary>
/// <remarks>
/// <para>
/// Postgres: partial, over the exchanges with a status, a few percent of them; built CONCURRENTLY.
/// A build cut short leaves an invalid index behind, which is renamed and dropped first, the same
/// steps as the migrations' ConcurrentIndex.
/// </para>
/// <para>
/// MySQL has no partial indexes, and SQL Server's filtered ones make writes fail from sessions with
/// other SET options, so both get a normal index. MySQL builds it online. SQL Server does where its
/// edition can; Standard and Express pause writes while they build, so they wait for
/// <see cref="BitweenOptions.MaintenanceQuietHourUtc"/>.
/// </para>
/// <para>
/// Status, then start time, then retried: every view lists one status newest first, which the first
/// two give in order. Retried last because "not retried" is a blank, and a test for blank can't keep
/// the start times in order, so ahead of them it left Postgres sorting thousands of failures to
/// show 25; measured on 3M exchanges, 18 s against 3 ms this way.
/// </para>
/// </remarks>
public static class XchangeStateIndex
{
    public const string Name = "ix_xchange_state";

    /// <summary>A build reads the whole table. Far longer than any query may take.</summary>
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromHours(1);

    /// <summary>
    /// Builds the index unless it is there already. True once it is there and usable; false while
    /// it waits for the quiet hour, or when Postgres reports it unusable and it must be built again.
    /// </summary>
    public static async Task<bool> EnsureAsync(BitweenDbContext db, BitweenOptions options,
        CancellationToken cancellation = default)
    {
        var sql = db.GetService<ISqlGenerationHelper>();
        var entity = db.Model.FindEntityType(typeof(Xchange))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var tableSql = sql.DelimitIdentifier(table.Name, table.Schema);
        string Column(string property) => sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(table)!);
        var columns = $"{Column(nameof(Xchange.Status))}, {Column(nameof(Xchange.StartedOn))}, {Column(nameof(Xchange.Retried))}";

        var previousTimeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(BuildTimeout);
        try
        {
            switch (options.DatabaseType?.ToLowerInvariant())
            {
                case "pgsql":
                {
                    var schema = table.Schema;
                    var leftover = Name + "_invalid";
                    await db.Database.ExecuteSqlRawAsync(
                        $@"DO $$
                           BEGIN
                               IF EXISTS (
                                   SELECT 1
                                   FROM pg_index i
                                   JOIN pg_class c ON c.oid = i.indexrelid
                                   JOIN pg_namespace n ON n.oid = c.relnamespace
                                   WHERE n.nspname = '{schema}' AND c.relname = '{Name}' AND NOT i.indisvalid
                               ) THEN
                                   ALTER INDEX {sql.DelimitIdentifier(Name, schema)} RENAME TO {leftover};
                               END IF;
                           END $$;", cancellation);
                    await db.Database.ExecuteSqlRawAsync(
                        $"DROP INDEX CONCURRENTLY IF EXISTS {sql.DelimitIdentifier(leftover, schema)};", cancellation);
                    await db.Database.ExecuteSqlRawAsync(
                        $"CREATE INDEX CONCURRENTLY IF NOT EXISTS {Name} ON {tableSql} ({columns}) " +
                        $"WHERE {Column(nameof(Xchange.Status))} IS NOT NULL;", cancellation);
                    var valid = await db.Database.SqlQueryRaw<bool>(
                        $@"SELECT i.indisvalid AS ""Value""
                           FROM pg_index i
                           JOIN pg_class c ON c.oid = i.indexrelid
                           JOIN pg_namespace n ON n.oid = c.relnamespace
                           WHERE n.nspname = '{schema}' AND c.relname = '{Name}'").SingleAsync(cancellation);

                    // Postgres knows nothing yet of how the new columns are spread - the fill changes too
                    // few rows to set off its own re-count - and guessing, it walked millions of
                    // exchanges for the rarest statuses. A sample, so seconds on any size.
                    if (valid)
                        await db.Database.ExecuteSqlRawAsync($"ANALYZE {tableSql};", cancellation);
                    return valid;
                }

                case "mysql":
                {
                    var exists = await db.Database.SqlQueryRaw<int>(
                        $@"SELECT COUNT(*) AS Value FROM information_schema.statistics
                           WHERE table_schema = DATABASE() AND table_name = '{table.Name}' AND index_name = '{Name}'")
                        .SingleAsync(cancellation);
                    // A MySQL 8 build is all or nothing, so there is never a half-built one to clear.
                    if (exists == 0)
                        await db.Database.ExecuteSqlRawAsync(
                            $"ALTER TABLE {tableSql} ADD INDEX {Name} ({columns}), ALGORITHM=INPLACE, LOCK=NONE;",
                            cancellation);
                    return true;
                }

                case "mssql":
                {
                    var exists = await db.Database.SqlQueryRaw<int>(
                        $"SELECT COUNT(*) AS Value FROM sys.indexes WHERE name = '{Name}' AND object_id = OBJECT_ID('{tableSql}')")
                        .SingleAsync(cancellation);
                    if (exists > 0) return true;

                    var online = !await NeedsQuietHourAsync(db, options, cancellation);
                    if (!online && DateTime.UtcNow.Hour != options.MaintenanceQuietHourUtc) return false;

                    // Transactional, so a build cut short leaves nothing behind.
                    await db.Database.ExecuteSqlRawAsync(
                        $"CREATE INDEX {sql.DelimitIdentifier(Name)} ON {tableSql} ({columns}) " +
                        $"WITH (ONLINE = {(online ? "ON" : "OFF")});", cancellation);
                    return true;
                }

                default:
                    throw new NotSupportedException($"No exchange status index for database type '{options.DatabaseType}'.");
            }
        }
        finally
        {
            db.Database.SetCommandTimeout(previousTimeout);
        }
    }

    /// <summary>
    /// Whether this SQL Server edition can build an index while writes go on: Enterprise and Developer
    /// (3), Azure SQL Database (5) and Managed Instance (8). Standard and Express can't.
    /// </summary>
    public static bool BuildsOnline(int engineEdition) => engineEdition is 3 or 5 or 8;

    /// <summary>
    /// Whether this database builds the index only in <see cref="BitweenOptions.MaintenanceQuietHourUtc"/>:
    /// SQL Server editions that can't build it online.
    /// </summary>
    public static async Task<bool> NeedsQuietHourAsync(BitweenDbContext db, BitweenOptions options,
        CancellationToken cancellation = default)
    {
        if (!string.Equals(options.DatabaseType, "MsSql", StringComparison.OrdinalIgnoreCase)) return false;
        var edition = await db.Database.SqlQueryRaw<int>(
            "SELECT CAST(SERVERPROPERTY('EngineEdition') AS int) AS Value").SingleAsync(cancellation);
        return !BuildsOnline(edition);
    }
}
