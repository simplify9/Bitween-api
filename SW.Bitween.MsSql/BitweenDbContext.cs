using Microsoft.EntityFrameworkCore;
using SW.PrimitiveTypes;
using SW.Scheduler.SqlServer;

namespace SW.Bitween.MsSql
{
    public class BitweenDbContext : Bitween.BitweenDbContext
    {
        public BitweenDbContext(DbContextOptions options, RequestContext requestContext, IPublish publish)
            : base(options, requestContext, publish) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.UseSchedulerSqlServer();
        }
    }
}
