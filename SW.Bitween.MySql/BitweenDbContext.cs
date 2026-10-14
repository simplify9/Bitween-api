using Microsoft.EntityFrameworkCore;
using SW.PrimitiveTypes;
using SW.Scheduler.MySql;

namespace SW.Bitween.MySql
{
    public class BitweenDbContext(DbContextOptions options, RequestContext requestContext,
        IPublish publish, IInfolinkCache cache = null) : Bitween.BitweenDbContext(options, requestContext, publish, cache)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.UseSchedulerMySql();
        }
    }
}
