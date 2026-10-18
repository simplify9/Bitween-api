using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SW.PrimitiveTypes;
using Testcontainers.PostgreSql;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// A database whose promoted values index was dropped by hand - the way to stop long promoted values
/// failing to save before DropPromotedValuesIndex dropped it for everyone. That migration assumed the
/// index was there, so on such a database it failed and the app crash-looped on startup.
/// </summary>
public sealed class HandDroppedIndexMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task A_database_without_the_promoted_values_index_migrates_to_the_latest()
    {
        await using var db = CreateDbContext();
        await db.GetService<IMigrator>().MigrateAsync("20260930150916_ExchangeRetention");
        await db.Database.ExecuteSqlRawAsync("DROP INDEX infolink.ix_xchange_promoted_properties_properties_raw;");

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    private PgSql.BitweenDbContext CreateDbContext()
    {
        var dataSource = new NpgsqlDataSourceBuilder(_postgres.GetConnectionString());
        dataSource.EnableDynamicJson();
        var options = new DbContextOptionsBuilder<PgSql.BitweenDbContext>()
            .UseSnakeCaseNamingConvention()
            .UseNpgsql(dataSource.Build(), b =>
            {
                b.MigrationsHistoryTable("_ef_migrations_history", PgSql.BitweenDbContext.Schema);
                b.MigrationsAssembly(typeof(PgSql.DbType).Assembly.FullName);
            })
            .Options;

        return new PgSql.BitweenDbContext(options, new RequestContext(), new NoPublish());
    }
}
