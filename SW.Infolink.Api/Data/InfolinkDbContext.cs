using Microsoft.EntityFrameworkCore;
using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Infolink
{
    public class InfolinkDbContext : DbContext
    {
        private readonly RequestContext requestContext;
        private readonly IPublish publish;

        public const string ConnectionString = "InfolinkDb";

        public InfolinkDbContext(DbContextOptions options, RequestContext requestContext, IPublish publish) : base(options)
        {
            this.requestContext = requestContext;
            this.publish = publish;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Document>(b =>
            {
                b.ToTable("Documents");
                b.Property(p => p.Id).ValueGeneratedNever();
                b.Property(p => p.Name).HasMaxLength(100).IsUnicode(false).IsRequired();
                b.Property(p => p.BusMessageTypeName).IsUnicode(false).HasMaxLength(500);
                b.Property(p => p.PromotedProperties).StoreAsJson();

                b.HasIndex(p => p.Name).IsUnique();
                b.HasIndex(p => p.BusMessageTypeName).IsUnique();

                b.HasMany<Subscription>().WithOne().HasForeignKey(p => p.DocumentId).OnDelete(DeleteBehavior.Restrict);
                b.HasMany<Xchange>().WithOne().HasForeignKey(p => p.DocumentId).OnDelete(DeleteBehavior.Restrict);

                b.HasData(new Document(Document.AggregationDocumentId, "Aggregation Document"));

            });

            modelBuilder.Entity<Partner>(b =>
            {
                b.ToTable("Partners");
                b.Metadata.SetNavigationAccessMode(PropertyAccessMode.Field);
                b.Property(p => p.Name).IsRequired().IsUnicode(false).HasMaxLength(200);
                b.HasMany(p => p.Subscriptions).WithOne().IsRequired(false).HasForeignKey(p => p.PartnerId).OnDelete(DeleteBehavior.Restrict);
                b.OwnsMany(p => p.ApiCredentials, apicred =>
                {
                    apicred.ToTable("PartnerApiCredentials");
                    apicred.Property(p => p.Name).IsRequired().HasMaxLength(500);
                    apicred.Property(p => p.Key).IsRequired().IsUnicode(false).HasMaxLength(500);
                    apicred.HasIndex(p => p.Key).IsUnique();
                    apicred.WithOwner().HasForeignKey("PartnerId");

                    apicred.HasData(
                        new
                        {
                            Id = 1,
                            PartnerId = Partner.SystemId,
                            Name = "default",
                            Key = "7facc758283844b49cc4ffd26a75b1de",
                        });
                });

                b.HasData(
                    new
                    {
                        Id = Partner.SystemId,
                        Name = "SYSTEM"
                    });

            });

            modelBuilder.Entity<Subscription>(b =>
            {
                b.ToTable("Subscriptions");
                b.Property(p => p.Name).HasMaxLength(100).IsRequired();
                //b.OwnsMany(p => p.AggregationSchedules, schedules => schedules.BuildSchedule("SubscriptionAggregationSchedules"));
                b.OwnsMany(p => p.Schedules, schedules =>
                {
                    schedules.ToTable("SubscriptionSchedules");
                    schedules.Property(p => p.On).HasConversion<long>();
                    schedules.Property(p => p.Recurrence).HasConversion<byte>();
                });

                b.Property(p => p.HandlerProperties).StoreAsJson();
                b.Property(p => p.MapperProperties).StoreAsJson();
                b.Property(p => p.ReceiverProperties).StoreAsJson();
                b.Property(p => p.ValidatorProperties).StoreAsJson();
                b.Property(p => p.DocumentFilter).StoreAsJson();

                b.Property(p => p.ResponseMessageTypeName).IsUnicode(false).HasMaxLength(500);

                b.Property(p => p.MapperId).HasMaxLength(200).IsUnicode(false);
                b.Property(p => p.HandlerId).HasMaxLength(200).IsUnicode(false);
                b.Property(p => p.ReceiverId).HasMaxLength(200).IsUnicode(false);
                b.Property(p => p.ValidatorId).HasMaxLength(200).IsUnicode(false);

                b.Property(p => p.Type).HasConversion<byte>();
                b.Property(p => p.AggregationTarget).HasConversion<byte>();

                b.HasOne<Subscription>().WithMany().HasForeignKey(p => p.ResponseSubscriptionId).IsRequired(false).HasConstraintName("FK_Subscriptions_RespSub"). OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Subscription>().WithMany().HasForeignKey(p => p.AggregationForId).IsRequired(false).HasConstraintName("FK_Subscriptions_AggFor").OnDelete(DeleteBehavior.Restrict);

            });

            modelBuilder.Entity<Xchange>(b =>
            {
                b.ToTable("Xchanges");
                b.Property(p => p.Id).IsUnicode(false).HasMaxLength(50);
                b.Property(p => p.RetryFor).IsUnicode(false).HasMaxLength(50);
                b.Property(p => p.References).IsSeparatorDelimited().HasMaxLength(1024);
                b.Property(p => p.InputHash).IsRequired().IsUnicode(false).HasMaxLength(50);
                b.Property(p => p.InputName).HasMaxLength(200);
                b.Property(p => p.MapperId).HasMaxLength(200).IsUnicode(false);
                b.Property(p => p.HandlerId).HasMaxLength(200).IsUnicode(false);
                b.Property(p => p.HandlerProperties).StoreAsJson();
                b.Property(p => p.MapperProperties).StoreAsJson();
                b.Property(p => p.InputContentType).IsUnicode(false).HasMaxLength(200);
                b.Property(p => p.ResponseMessageTypeName).IsUnicode(false).HasMaxLength(500);



                b.HasIndex(i => i.InputHash);
                b.HasIndex(i => i.SubscriptionId);
                b.HasIndex(i => i.StartedOn);
                b.HasIndex(i => i.RetryFor);

            });

            modelBuilder.Entity<XchangeResult>(b =>
            {
                b.ToTable("XchangeResults");
                b.Property(p => p.Id).IsUnicode(false).HasMaxLength(50);
                b.Property(p => p.OutputHash).IsUnicode(false).HasMaxLength(50);
                b.Property(p => p.OutputName).HasMaxLength(200);
                b.Property(p => p.ResponseHash).IsUnicode(false).HasMaxLength(50);
                b.Property(p => p.ResponseName).HasMaxLength(200);
                b.Property(p => p.ResponseContentType).IsUnicode(false).HasMaxLength(200);
                b.Property(p => p.OutputContentType).IsUnicode(false).HasMaxLength(200);



                b.HasOne<Xchange>().WithOne().HasForeignKey<XchangeResult>(p => p.Id).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<XchangeDelivery>(b =>
            {
                b.ToTable("XchangeDeliveries");
                b.Property(p => p.Id).IsUnicode(false).HasMaxLength(50);
                b.HasIndex(i => i.DeliveredOn);
                b.HasOne<Xchange>().WithOne().HasForeignKey<XchangeDelivery>(p => p.Id).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<XchangeAggregation>(b =>
            {
                b.ToTable("XchangeAggregations");
                b.Property(p => p.Id).IsUnicode(false).HasMaxLength(50);
                b.Property(p => p.AggregationXchangeId).IsRequired().IsUnicode(false).HasMaxLength(50);

                b.HasIndex(i => i.AggregationXchangeId);

                b.HasOne<Xchange>().WithOne().HasForeignKey<XchangeAggregation>(p => p.Id).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<XchangePromotedProperties>(b =>
            {
                b.ToTable("XchangePromotedProperties");
                b.Property(p => p.Id).IsUnicode(false).HasMaxLength(50);
                b.Property(p => p.Properties).StoreAsJson();
                b.Property(p => p.Hits).IsSeparatorDelimited().IsUnicode(false).HasMaxLength(2000);

                b.HasOne<Xchange>().WithOne().HasForeignKey<XchangePromotedProperties>(p => p.Id).OnDelete(DeleteBehavior.Cascade);
            });

        }

        async public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            //using var transaction = await Database.BeginTransactionAsync();
            var affectedRecords = await base.SaveChangesAsync(cancellationToken);
            await ChangeTracker.PublishDomainEvents(publish);
            //await transaction.CommitAsync();
            return affectedRecords;
        }
    }
}
