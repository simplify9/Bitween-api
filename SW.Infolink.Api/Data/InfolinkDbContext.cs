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

            //if (configuration["Database"]?.ToLower() == "mssql")
            //{
            //    schema = "infolink";
            //}

            //modelBuilder.Entity<Adapter>(b =>
            //{

            //    b.ToTable("Adapters");
            //    b.Property(p => p.Name).IsRequired().IsUnicode(false).HasMaxLength(200);
            //    b.Property(p => p.ServerlessId).IsUnicode(false).HasMaxLength(200);
            //    b.Property(p => p.Properties).StoreAsJson();

            //    //b.HasData(
            //    //    new Adapter(AdapterType.Mapper, "JsonToCsvMapper", "infolink.jsontocsvmapper") { Id = 1 },
            //    //    new Adapter(AdapterType.Mapper, "JsonToXmlMapper", "infolink.jsontoxmlmapper") { Id = 2 },
            //    //    new Adapter(AdapterType.Handler, "As2FileHandler", "infolink.as2filehandler") { Id = 3 },
            //    //    new Adapter(AdapterType.Handler, "FtpFileHandler", "infolink.ftpfilehandler") { Id = 4 },
            //    //    new Adapter(AdapterType.Handler, "HttpFileHandler", "infolink.httpfilehandler") { Id = 5 },
            //    //    new Adapter(AdapterType.Handler, "SftpFileHandler", "infolink.sftpfilehandler") { Id = 6 },
            //    //    new Adapter(AdapterType.Handler, "S3FileHandler", "infolink.s3filehandler") { Id = 7 },
            //    //    new Adapter(AdapterType.Handler, "AzureBlobFileHandler", "infolink.azureblobfilehandler") { Id = 8 },
            //    //    new Adapter(AdapterType.Receiver, "AzureBlobFileReceiver", "infolink.azureBlobfilereceiver") { Id = 9 },
            //    //    new Adapter(AdapterType.Receiver, "SftpFileReceiver", "infolink.sftpfilereceiver") { Id = 10 },
            //    //    new Adapter(AdapterType.Receiver, "FtpFileReceiver", "infolink.ftpfilereceiver") { Id = 11 }
            //    //    );
            //});

            modelBuilder.Entity<Document>(b =>
            {
                b.ToTable("Documents");
                b.Property(p => p.Id).ValueGeneratedNever();
                b.Property(p => p.Name).HasMaxLength(100).IsUnicode(false).IsRequired();
                b.Property(p => p.BusMessageTypeName).IsUnicode(false).HasMaxLength(500);
                b.Property(p => p.PromotedProperties).StoreAsJson();

                b.HasIndex(p => p.Name).IsUnique();
            });

            modelBuilder.Entity<Partner>(b =>
            {
                b.ToTable("Partners");
                b.Metadata.SetNavigationAccessMode(PropertyAccessMode.Field);
                b.Property(p => p.Name).IsRequired().IsUnicode(false).HasMaxLength(200);
                b.HasMany(p => p.Subscriptions).WithOne().IsRequired(false).HasForeignKey("PartnerId").OnDelete(DeleteBehavior.SetNull);
                b.OwnsMany(p => p.ApiCredentials, apicred =>
                {
                    apicred.ToTable("PartnerApiCredentials");
                    apicred.Property(p => p.Name).IsRequired().HasMaxLength(500);
                    apicred.Property(p => p.Key).IsRequired().IsUnicode(false).HasMaxLength(500);
                    apicred.HasIndex(p => p.Key).IsUnique();
                    apicred.WithOwner().HasForeignKey("PartnerId");
                });
            });

            modelBuilder.Entity<Subscription>(b =>
            {
                b.ToTable("Subscribers");
                b.Property(p => p.Name).HasMaxLength(100).IsRequired();
                b.OwnsMany(p => p.Schedules, schedules => schedules.BuildSchedule("SubscriberSchedules"));
                b.Property(p => p.HandlerProperties).StoreAsJson();
                b.Property(p => p.MapperProperties).StoreAsJson();
                b.Property(p => p.DocumentFilter).StoreAsJson();
                b.Property(p => p.MapperId).HasMaxLength(200).IsUnicode(false);
                b.Property(p => p.HandlerId).HasMaxLength(200).IsUnicode(false);
                b.HasMany(p => p.Receivers).WithOne(p => p.Subscription).IsRequired(true).HasForeignKey("SubscriberId").OnDelete(DeleteBehavior.Cascade);
                b.HasOne<Subscription>().WithOne().HasForeignKey<Subscription>(p => p.ResponseSubscriptionId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Receiver>(b =>
            {
                b.ToTable("Receivers");
                //b.Property(p => p.Id).ValueGeneratedNever();
                //b.Property(p => p.Id).HasSequenceGenerator();
                b.Property(p => p.Name).HasMaxLength(100).IsRequired();
                b.OwnsMany(p => p.Schedules, schedules => schedules.BuildSchedule("ReceiverSchedules"));
                b.Property(p => p.Properties).StoreAsJson();
                b.Property(p => p.ReceiverId).HasMaxLength(200).IsUnicode(false).IsRequired();

            });

            modelBuilder.Entity<Xchange>(b =>
            {
                b.ToTable("Xchanges");
                b.Property(p => p.Id).IsUnicode(false).HasMaxLength(50);
                b.Property(p => p.References).IsSeparatorDelimited().HasMaxLength(1024);
                b.Property(p => p.InputFileHash).IsRequired().IsUnicode(false).HasMaxLength(50);
                b.Property(p => p.InputFileName).HasMaxLength(200);
                b.Property(p => p.MapperId).HasMaxLength(200).IsUnicode(false);
                b.Property(p => p.HandlerId).HasMaxLength(200).IsUnicode(false);
                b.Property(p => p.HandlerProperties).StoreAsJson();
                b.Property(p => p.MapperProperties).StoreAsJson();

                b.HasIndex(i => i.InputFileHash);
                b.HasIndex(i => i.DeliverOn);
                b.HasIndex(i => i.SubscriptionId);

            });

            modelBuilder.Entity<XchangeResult>(b =>
            {
                b.ToTable("XchangeResults");
                b.Property(p => p.Id).IsUnicode(false).HasMaxLength(50);
                b.Property(p => p.XchangeId).IsUnicode(false).HasMaxLength(50);
                b.HasOne<Xchange>().WithOne().HasForeignKey<XchangeResult>(p => p.XchangeId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<XchangeDelivery>(b =>
            {
                b.ToTable("XchangeDeliveries");
                b.Property(p => p.Id).IsUnicode(false).HasMaxLength(50);
                b.Property(p => p.XchangeId).IsUnicode(false).HasMaxLength(50);
                b.HasIndex(i => i.DeliveredOn);
                b.HasOne<Xchange>().WithOne().HasForeignKey<XchangeDelivery>(p => p.XchangeId).OnDelete(DeleteBehavior.Cascade);

            });

            modelBuilder.Entity<XchangePromotedProperties>(b =>
            {
                b.ToTable("XchangePromotedProperties");
                b.Property(p => p.Id).IsUnicode(false).HasMaxLength(50);
                b.Property(p => p.XchangeId).IsUnicode(false).HasMaxLength(50);
                b.Property(p => p.Properties).StoreAsJson();
                b.HasOne<Xchange>().WithOne().HasForeignKey<XchangePromotedProperties>(p => p.XchangeId).OnDelete(DeleteBehavior.Cascade);

            });

            //modelBuilder.Entity<XchangeBlob>(b =>
            //{
            //    b.ToTable("XchangeFiles");
            //    b.HasKey(k => new { k.Id, k.Type });
            //    b.Property(p => p.Type).HasConversion<byte>();

            //});
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
