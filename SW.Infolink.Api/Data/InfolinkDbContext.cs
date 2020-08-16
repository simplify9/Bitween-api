using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Infolink
{
    public class InfolinkDbContext : DbContext
    {
        private readonly IConfiguration configuration;
        private readonly IDomainEventDispatcher domainEventDispatcher;

        public const string ConnectionString = "InfolinkDb";

        public InfolinkDbContext(DbContextOptions options, IConfiguration configuration, IDomainEventDispatcher domainEventDispatcher) : base(options)
        {
            this.configuration = configuration;
            this.domainEventDispatcher = domainEventDispatcher;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            string schema = null;
            if (configuration["Database"]?.ToLower() == "mssql")
            {
                schema = "infolink";
            }

            modelBuilder.Entity<Adapter>(b =>
            {

                b.ToTable("Adapters", schema);
                b.Property(p => p.Name).IsRequired().IsUnicode(false).HasMaxLength(200);
                b.Property(p => p.ServerlessId).IsUnicode(false).HasMaxLength(200);
                b.Property(p => p.Properties).StoreAsJson();

                b.HasData(
                    new Adapter(AdapterType.Mapper, "JsonToCsvMapper", "infolink.jsontocsvmapper"),
                    new Adapter(AdapterType.Mapper, "JsonToXmlMapper", "infolink.jsontoxmlmapper"),
                    new Adapter(AdapterType.Handler, "As2FileHandler", "infolink.as2filehandler"),
                    new Adapter(AdapterType.Handler, "FtpFileHandler", "infolink.ftpfilehandler"),
                    new Adapter(AdapterType.Handler, "HttpFileHandler", "infolink.httpfilehandler"),
                    new Adapter(AdapterType.Handler, "SftpFileHandler", "infolink.sftpfilehandler"),
                    new Adapter(AdapterType.Handler, "S3FileHandler", "infolink.s3filehandler"),
                    new Adapter(AdapterType.Handler, "AzureBlobFileHandler", "infolink.azureblobfilehandler"),
                    new Adapter(AdapterType.Receiver, "AzureBlobFileReceiver", "infolink.azureBlobfilereceiver"),
                    new Adapter(AdapterType.Receiver, "SftpFileReceiver", "infolink.sftpfilereceiver"),
                    new Adapter(AdapterType.Receiver, "FtpFileReceiver", "infolink.ftpfilereceiver")
                    );
            });

            modelBuilder.Entity<Document>(b =>
            {
                b.ToTable("Documents", schema);
                b.Property(p => p.Id).ValueGeneratedNever();
                b.Property(p => p.Name).HasMaxLength(100).IsRequired();
                b.Property(p => p.BusMessageTypeName).IsUnicode(false).HasMaxLength(500);
                b.Property(p => p.PromotedProperties).StoreAsJson();
            });

            modelBuilder.Entity<AccessKeySet>(b =>
            {
                b.ToTable("AccessKeySets", schema);
                b.Property(p => p.Name).IsRequired().IsUnicode(false).HasMaxLength(200);
                b.Property(p => p.Key1).HasMaxLength(1024).IsUnicode(false).IsRequired();
                b.Property(p => p.Key2).HasMaxLength(1024).IsUnicode(false).IsRequired();
            });

            modelBuilder.Entity<Subscriber>(b =>
            {
                b.ToTable("Subscribers", schema);
                b.Property(p => p.Name).HasMaxLength(100).IsRequired();
                b.OwnsMany(p => p.Schedules, schedules => schedules.BuildSchedule("SubscriberSchedules", schema));
                b.Property(p => p.Properties).StoreAsJson();
                b.Property(p => p.DocumentFilter).StoreAsJson();
            });

            modelBuilder.Entity<Receiver>(b =>
            {
                b.ToTable("Receivers", schema);
                b.Property(p => p.Id).ValueGeneratedNever();
                b.Property(p => p.Name).HasMaxLength(100).IsRequired();
                b.OwnsMany(p => p.Schedules, schedules => schedules.BuildSchedule("ReceiverSchedules", schema));
                b.Property(p => p.Properties).StoreAsJson();

            });

            modelBuilder.Entity<Xchange>(b =>
            {
                b.ToTable("Xchanges", schema);
                b.Property(p => p.References).IsSeparatorDelimited().HasMaxLength(1024);
                b.Property(p => p.InputFileHash).IsRequired().IsUnicode(false).HasMaxLength(50);
                b.Property(p => p.InputFileName).HasMaxLength(200);

                b.HasIndex(i => i.InputFileHash);
                b.HasIndex(i => i.DeliveredOn);
                b.HasIndex(i => i.DeliverOn);
                b.HasIndex(i => i.SubscriberId);

            });

            modelBuilder.Entity<XchangeBlob>(b =>
            {
                b.ToTable("XchangeFiles", schema);
                b.HasKey(k => new { k.Id, k.Type });
                b.Property(p => p.Type).HasConversion<byte>();

            });
        }

        async public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var transaction = Database.BeginTransaction();
                var affectedRecords = await base.SaveChangesAsync(cancellationToken);

                await ChangeTracker.DispatchDomainEvents(domainEventDispatcher);

                await transaction.CommitAsync();

                return affectedRecords;

            }
            catch (DbUpdateException dbUpdateException)
            {
                if (dbUpdateException.InnerException == null)
                    throw new SWException($"Data Error: {dbUpdateException.Message}");
                else
                    throw new SWException($"Data Error: {dbUpdateException.InnerException.Message}");
            }
        }
    }
}
