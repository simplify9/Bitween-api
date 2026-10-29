using Microsoft.EntityFrameworkCore;
using SW.PrimitiveTypes;
using SW.Scheduler.MySql;

namespace SW.Bitween.MySql
{
    public class BitweenDbContext(DbContextOptions options, RequestContext requestContext,
        IPublish publish, IInfolinkCache cache = null, OutboxAcknowledger outboxAcknowledger = null)
        : Bitween.BitweenDbContext(options, requestContext, publish, cache, outboxAcknowledger)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.UseSchedulerMySql();

            // MySQL has no filtered index; unpublished rows lead with a null PublishedOn, which this
            // finds first, and old published rows are cleaned up by the same index.
            modelBuilder.Entity<Domain.OutboxMessage>().HasIndex(p => new { p.PublishedOn, p.CreatedOn });
        }
    }
}
