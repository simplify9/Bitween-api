using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using SW.Scheduler.SqlServer;

namespace SW.Bitween.MsSql
{
    public class BitweenDbContext : Bitween.BitweenDbContext
    {
        /// <summary>Backs <see cref="Document"/> ids — see the note in OnModelCreating.</summary>
        public const string DocumentIdSequence = "DocumentIds";

        public BitweenDbContext(DbContextOptions options, RequestContext requestContext, IPublish publish)
            : base(options, requestContext, publish) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.UseSchedulerSqlServer();

            // Documents.Id became database-generated after the table already existed. SQL Server
            // cannot add IDENTITY to an existing column — EF refuses outright ("to change the
            // IDENTITY property of a column, the column needs to be dropped and recreated") — and
            // rebuilding Documents would mean dropping the foreign keys Subscriptions and Xchanges
            // hold against it. A sequence-backed default generates ids the same way with no table
            // rebuild. Postgres uses identity and MySQL AUTO_INCREMENT, which both alter in place.
            modelBuilder.Entity<Document>().Property(p => p.Id).UseSequence(DocumentIdSequence);
        }
    }
}
