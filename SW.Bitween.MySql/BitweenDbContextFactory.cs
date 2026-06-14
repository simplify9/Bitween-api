using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SW.Bitween.MySql
{
    public class BitweenDbContextFactory : IDesignTimeDbContextFactory<BitweenDbContext>
    {
        public BitweenDbContext CreateDbContext(string[] args)
        {
            var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__BitweenDb")
                ?? "Server=localhost;Port=3307;Database=bitween;User=root;Password=mysql";

            var optionsBuilder = new DbContextOptionsBuilder<BitweenDbContext>();
            optionsBuilder.UseMySql(connStr, new MySqlServerVersion(new Version(8, 0, 18)), b =>
            {
                b.MigrationsAssembly(typeof(DbType).Assembly.FullName);
            });

            return new BitweenDbContext(optionsBuilder.Options, null!, null!);
        }
    }
}
