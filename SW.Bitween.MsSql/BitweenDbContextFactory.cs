using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SW.Bitween.MsSql
{
    public class BitweenDbContextFactory : IDesignTimeDbContextFactory<BitweenDbContext>
    {
        public BitweenDbContext CreateDbContext(string[] args)
        {
            var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__BitweenDb")
                ?? "Server=localhost,1433;Database=bitween;User Id=sa;Password=Pass@word123;TrustServerCertificate=True";

            var optionsBuilder = new DbContextOptionsBuilder<BitweenDbContext>();
            optionsBuilder.UseSqlServer(connStr, b =>
            {
                b.MigrationsAssembly(typeof(DbType).Assembly.FullName);
            });

            return new BitweenDbContext(optionsBuilder.Options, null!, null!);
        }
    }
}
