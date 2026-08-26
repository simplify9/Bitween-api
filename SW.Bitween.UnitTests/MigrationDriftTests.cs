using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests;

/// <summary>
/// Asserts each provider's model still matches its newest migration.
///
/// This is the same check EF runs during <c>Database.Migrate()</c> at startup: when the model and
/// the latest migration's snapshot disagree, the app throws PendingModelChangesWarning and the
/// process dies before it serves anything. Without a test, that only ever surfaces as a pod
/// crash-looping in whichever environment was unlucky enough to deploy first.
///
/// Every provider is covered deliberately. MsSql and MySql inherit the base context's seed data
/// (they call base.OnModelCreating; PgSql re-declares the model instead), so a change to shared
/// seed values silently drifts their snapshots even when PgSql's has been regenerated — which is
/// exactly what happened when the seed timestamp fix was first applied to PgSql alone.
///
/// No database is touched: this compares the in-memory model against the compiled-in snapshot,
/// so the connection strings below are never opened.
/// </summary>
[TestClass]
public class MigrationDriftTests
{
    [TestMethod]
    public void PgSql_model_matches_its_latest_migration() =>
        AssertNoPendingChanges(BuildPgSql());

    [TestMethod]
    public void MsSql_model_matches_its_latest_migration() =>
        AssertNoPendingChanges(BuildMsSql());

    [TestMethod]
    public void MySql_model_matches_its_latest_migration() =>
        AssertNoPendingChanges(BuildMySql());

    private static void AssertNoPendingChanges(DbContext dbContext)
    {
        using (dbContext)
        {
            Assert.IsFalse(dbContext.Database.HasPendingModelChanges(),
                $"{dbContext.GetType().FullName} has model changes that no migration covers. "
                + "The app will refuse to start against this provider. Generate a migration for it — "
                + "and remember a shared-model change usually needs one per provider.");
        }
    }

    // Mirrors Startup's registration for each provider: the migrations assembly (and, for PgSql,
    // the naming convention and history table) all take part in the comparison, so a test that
    // configured them differently would be comparing something the app never builds.

    private static PgSql.BitweenDbContext BuildPgSql()
    {
        var options = new DbContextOptionsBuilder<PgSql.BitweenDbContext>()
            .UseSnakeCaseNamingConvention()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused", b =>
            {
                b.MigrationsHistoryTable("_ef_migrations_history", PgSql.BitweenDbContext.Schema);
                b.MigrationsAssembly(typeof(PgSql.DbType).Assembly.FullName);
            })
            .Options;

        return new PgSql.BitweenDbContext(options, new RequestContext(), null);
    }

    private static MsSql.BitweenDbContext BuildMsSql()
    {
        var options = new DbContextOptionsBuilder<MsSql.BitweenDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=true",
                b => b.MigrationsAssembly(typeof(MsSql.DbType).Assembly.FullName))
            .Options;

        return new MsSql.BitweenDbContext(options, new RequestContext(), null);
    }

    private static MySql.BitweenDbContext BuildMySql()
    {
        // An explicit server version, never AutoDetect — the latter opens a connection.
        var options = new DbContextOptionsBuilder<MySql.BitweenDbContext>()
            .UseMySql("Server=unused;Database=unused;User=unused;Password=unused",
                new MySqlServerVersion(new Version(8, 0, 18)),
                b => b.MigrationsAssembly(typeof(MySql.DbType).Assembly.FullName))
            .Options;

        return new MySql.BitweenDbContext(options, new RequestContext(), null);
    }
}
