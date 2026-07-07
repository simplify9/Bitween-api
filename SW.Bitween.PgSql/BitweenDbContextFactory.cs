using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace SW.Bitween.PgSql
{
    public class BitweenDbContextFactory : IDesignTimeDbContextFactory<BitweenDbContext>
    {
        public BitweenDbContext CreateDbContext(string[] args)
        {
            var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__BitweenDb")
                ?? "Host=localhost;Port=5432;Database=bitween;Username=postgres;Password=postgres";

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connStr);
            dataSourceBuilder.EnableDynamicJson();
            var dataSource = dataSourceBuilder.Build();

            var optionsBuilder = new DbContextOptionsBuilder<BitweenDbContext>();
            optionsBuilder
                .UseSnakeCaseNamingConvention()
                .UseNpgsql(dataSource, b =>
                {
                    b.MigrationsHistoryTable("_ef_migrations_history", BitweenDbContext.Schema);
                    b.MigrationsAssembly(typeof(DbType).Assembly.FullName);
                });

            return new BitweenDbContext(optionsBuilder.Options, null!, null!);
        }
    }
}
