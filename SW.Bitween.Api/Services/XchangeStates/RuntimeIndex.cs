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
/// An index on the exchanges built by a scheduled job and never by a migration, so no deploy waits
/// for it on a big table. <see cref="XchangeStateIndex"/> and <see cref="XchangePartnerIndex"/> are
/// the two.
/// </summary>
/// <remarks>
/// <para>
/// Postgres: partial, over the rows where <see cref="PostgresOnlyWhereSet"/> has a value, and built
/// CONCURRENTLY. A build cut short leaves an invalid index behind, which is renamed and dropped first,
/// the same steps as the migrations' ConcurrentIndex. An advisory lock keeps it to one node at a time:
/// a node starting up registers its jobs again, which lets the next run start while this one is still
/// building, and a build under way is not valid yet, the same as one cut short, so a second run took
/// it for a leftover and dropped it.
/// </para>
/// <para>
/// MySQL has no partial indexes, and SQL Server's filtered ones make writes fail from sessions with
/// other SET options, so both get a normal index. MySQL builds it online. SQL Server does where its
/// edition can; Standard and Express pause writes while they build, so they wait for
/// <see cref="BitweenOptions.MaintenanceQuietHourUtc"/>.
/// </para>
/// </remarks>
/// <param name="Name">The index's name, the same on every database.</param>
/// <param name="BuildLock">The Postgres advisory lock its build holds; one per index.</param>
/// <param name="Columns">Exchange properties, in index order.</param>
/// <param name="PostgresOnlyWhereSet">The property a Postgres index covers only the rows with a value in.</param>
public record RuntimeIndex(string Name, long BuildLock, string[] Columns, string PostgresOnlyWhereSet)
{
    /// <summary>A build reads the whole table. Far longer than any query may take.</summary>
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromHours(1);

    /// <summary>
    /// Builds the index unless it is there already. True once it is there and usable; false while
    /// it waits for the quiet hour, while another node is building it, or when Postgres reports it
    /// unusable and it must be built again.
    /// </summary>
    public async Task<bool> EnsureAsync(BitweenDbContext db, BitweenOptions options, CancellationToken cancellation = default)
    {
        var sql = db.GetService<ISqlGenerationHelper>();
        var entity = db.Model.FindEntityType(typeof(Xchange))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var tableSql = sql.DelimitIdentifier(table.Name, table.Schema);
        string Column(string property) => sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(table)!);
        var columns = string.Join(", ", Columns.Select(Column));

        var previousTimeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(BuildTimeout);
        try
        {
            switch (options.DatabaseType?.ToLowerInvariant())
            {
                case "pgsql":
                {
                    // Held for the connection, so a node that dies mid-build lets go of it.
                    await db.Database.OpenConnectionAsync(cancellation);
                    try
                    {
                        if (!await db.Database.SqlQueryRaw<bool>(
                                $@"SELECT pg_try_advisory_lock({BuildLock}) AS ""Value""").SingleAsync(cancellation))
                            return false;
                        try
                        {
                            return await BuildPostgresAsync(db, sql, table, tableSql, columns,
                                Column(PostgresOnlyWhereSet), cancellation);
                        }
                        finally
                        {
                            await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_unlock({BuildLock});");
                        }
                    }
                    finally
                    {
                        await db.Database.CloseConnectionAsync();
                    }
                }

                case "mysql":
                {
                    async Task<bool> Exists() => await db.Database.SqlQueryRaw<int>(
                            $@"SELECT COUNT(*) AS Value FROM information_schema.statistics
                               WHERE table_schema = DATABASE() AND table_name = '{table.Name}' AND index_name = '{Name}'")
                        .SingleAsync(cancellation) > 0;
                    // A MySQL 8 build is all or nothing, so there is never a half-built one to clear.
                    if (await Exists()) return true;
                    return await CreateUnlessBuiltMeanwhile(Exists, () => db.Database.ExecuteSqlRawAsync(
                        $"ALTER TABLE {tableSql} ADD INDEX {Name} ({columns}), ALGORITHM=INPLACE, LOCK=NONE;",
                        cancellation));
                }

                case "mssql":
                {
                    async Task<bool> Exists() => await db.Database.SqlQueryRaw<int>(
                            $"SELECT COUNT(*) AS Value FROM sys.indexes WHERE name = '{Name}' AND object_id = OBJECT_ID('{tableSql}')")
                        .SingleAsync(cancellation) > 0;
                    if (await Exists()) return true;

                    var online = !await NeedsQuietHourAsync(db, options, cancellation);
                    if (!online && DateTime.UtcNow.Hour != options.MaintenanceQuietHourUtc) return false;

                    // Transactional, so a build cut short leaves nothing behind.
                    return await CreateUnlessBuiltMeanwhile(Exists, () => db.Database.ExecuteSqlRawAsync(
                        $"CREATE INDEX {sql.DelimitIdentifier(Name)} ON {tableSql} ({columns}) " +
                        $"WITH (ONLINE = {(online ? "ON" : "OFF")});", cancellation));
                }

                default:
                    throw new NotSupportedException($"No exchange index {Name} for database type '{options.DatabaseType}'.");
            }
        }
        finally
        {
            db.Database.SetCommandTimeout(previousTimeout);
        }
    }

    /// <summary>
    /// Creates it, and when that fails, asks whether it is there after all. MySQL and SQL Server have
    /// no lock held across the check and the build the way Postgres's advisory one is, so another
    /// node's run can build it in between; its "already exists" is then this run's success too.
    /// </summary>
    private static async Task<bool> CreateUnlessBuiltMeanwhile(Func<Task<bool>> exists, Func<Task<int>> create)
    {
        try
        {
            await create();
            return true;
        }
        catch (System.Data.Common.DbException)
        {
            if (await exists()) return true;
            throw;
        }
    }

    private async Task<bool> BuildPostgresAsync(BitweenDbContext db, ISqlGenerationHelper sql,
        StoreObjectIdentifier table, string tableSql, string columns, string whereSetColumn, CancellationToken cancellation)
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
            $"WHERE {whereSetColumn} IS NOT NULL;", cancellation);
        var valid = await db.Database.SqlQueryRaw<bool>(
            $@"SELECT i.indisvalid AS ""Value""
               FROM pg_index i
               JOIN pg_class c ON c.oid = i.indexrelid
               JOIN pg_namespace n ON n.oid = c.relnamespace
               WHERE n.nspname = '{schema}' AND c.relname = '{Name}'").SingleAsync(cancellation);

        // Postgres may know nothing yet of how a newly filled column is spread, and guessing, it
        // walked millions of exchanges for the rarest values. A sample, so seconds on any size.
        if (valid)
            await db.Database.ExecuteSqlRawAsync($"ANALYZE {tableSql};", cancellation);
        return valid;
    }

    /// <summary>
    /// Whether this SQL Server edition can build an index while writes go on: Enterprise and Developer
    /// (3), Azure SQL Database (5) and Managed Instance (8). Standard and Express can't.
    /// </summary>
    public static bool BuildsOnline(int engineEdition) => engineEdition is 3 or 5 or 8;

    /// <summary>
    /// Whether this database builds these indexes only in <see cref="BitweenOptions.MaintenanceQuietHourUtc"/>:
    /// SQL Server editions that can't build them online.
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
