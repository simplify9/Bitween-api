using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using SW.Scheduler.SqlServer;

namespace SW.Bitween.MsSql
{
    public class BitweenDbContext(DbContextOptions options, RequestContext requestContext,
        IPublish publish, IInfolinkCache cache = null, OutboxAcknowledger outboxAcknowledger = null)
        : Bitween.BitweenDbContext(options, requestContext, publish, cache, outboxAcknowledger)
    {
        /// <summary>Backs <see cref="Document"/> ids - see the note in OnModelCreating.</summary>
        public const string DocumentIdSequence = "DocumentIds";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.UseSchedulerSqlServer();

            // Documents.Id became database-generated after the table already existed. SQL Server
            // cannot add IDENTITY to an existing column - EF refuses outright ("to change the
            // IDENTITY property of a column, the column needs to be dropped and recreated") - and
            // rebuilding Documents would mean dropping the foreign keys Subscriptions and Xchanges
            // hold against it. A sequence-backed default generates ids the same way with no table
            // rebuild. Postgres uses identity and MySQL AUTO_INCREMENT, which both alter in place.
            modelBuilder.Entity<Document>().Property(p => p.Id).UseSequence(DocumentIdSequence);

            // What the outbox dispatcher looks for: rows not yet published. Published rows are
            // deleted now, so the rows it covers are the only ones there normally are.
            modelBuilder.Entity<OutboxMessage>().HasIndex(p => p.CreatedOn).HasFilter("[PublishedOn] IS NULL");
        }
    }
}
