using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SW.Infolink.Domain.Accounts;

namespace SW.Infolink.PgSql
{
    public class InfolinkDbContext : Infolink.InfolinkDbContext
    {
        //private readonly RequestContext requestContext;
        //private readonly IPublish publish;

        public const string Schema = "infolink";

        public InfolinkDbContext(DbContextOptions options, RequestContext requestContext, IPublish publish) : base(
            options, requestContext, publish)
        {
            //this.requestContext = requestContext;
            //this.publish = publish;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            //base.OnModelCreating(modelBuilder);

            modelBuilder.HasDefaultSchema(Schema);

            modelBuilder.Entity<Document>(b =>
            {
                //b.ToTable("Documents");
                b.Property(p => p.Id).ValueGeneratedNever();
                b.Property(p => p.Name).HasMaxLength(100).IsRequired();
                b.Property(p => p.BusMessageTypeName).HasMaxLength(500);
                b.Property(p => p.PromotedProperties).StoreAsJson().HasColumnType("jsonb");
                b.Property(p => p.DisregardsUnfilteredMessages).IsRequired(false);

                b.HasIndex(p => p.Name).IsUnique();
                b.HasIndex(p => p.BusMessageTypeName).IsUnique();

                b.HasMany<Subscription>().WithOne().HasForeignKey(p => p.DocumentId).OnDelete(DeleteBehavior.Restrict);
                b.HasMany<Xchange>().WithOne().HasForeignKey(p => p.DocumentId).OnDelete(DeleteBehavior.Restrict);

                b.HasData(new Document(Document.AggregationDocumentId, "Aggregation Document"));
            });

            modelBuilder.Entity<RunFlagUpdater.RunningResult>(cr =>
            {
                cr.HasNoKey().ToView(null);
                cr.Property(c => c.IsRunning);
            });

            modelBuilder.Entity<Partner>(b =>
            {
                //b.ToTable("Partners");
                b.Metadata.SetNavigationAccessMode(PropertyAccessMode.Field);
                b.Property(p => p.Name).IsRequired().HasMaxLength(200);
                b.HasMany(p => p.Subscriptions).WithOne().IsRequired(false).HasForeignKey(p => p.PartnerId)
                    .OnDelete(DeleteBehavior.Restrict);
                b.OwnsMany(p => p.ApiCredentials, apicred =>
                {
                    apicred.ToTable("partner_api_credential");
                    apicred.Property(p => p.Name).IsRequired().HasMaxLength(500);
                    apicred.Property(p => p.Key).IsRequired().HasMaxLength(500);
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
                //b.ToTable("Subscriptions");
                b.Property(p => p.Name).HasMaxLength(100).IsRequired();
                //b.Property(p => p.AggregationSchedules).hast
                var ownedScheduleBuilder = b.OwnsMany(p => p.Schedules, sched =>
                {
                    sched.Property(p => p.On).HasConversion<long>();
                    sched.Property(p => p.Recurrence).HasConversion<byte>();
                    sched.ToTable("subscription_schedule");
                });


                b.Property(p => p.HandlerProperties).HasColumnType("jsonb");
                b.Property(p => p.MapperProperties).HasColumnType("jsonb");
                b.Property(p => p.ReceiverProperties).HasColumnType("jsonb");
                b.Property(p => p.ValidatorProperties).HasColumnType("jsonb");
                b.Property(p => p.DocumentFilter).HasColumnType("jsonb");

                b.Property(p => p.ResponseMessageTypeName).HasMaxLength(500);

                b.Property(p => p.MapperId).HasMaxLength(200);
                b.Property(p => p.HandlerId).HasMaxLength(200);
                b.Property(p => p.ReceiverId).HasMaxLength(200);
                b.Property(p => p.ValidatorId).HasMaxLength(200);

                b.Property(p => p.Type).HasConversion<byte>();
                b.Property(p => p.AggregationTarget).HasConversion<byte>();

                b.HasOne<Subscription>().WithMany().HasForeignKey(p => p.ResponseSubscriptionId).IsRequired(false)
                    .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_subscription_response_subscriber");

                b.HasOne<Subscription>().WithMany().HasForeignKey(p => p.AggregationForId).IsRequired(false)
                    .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_subscription_aggregation_for");
            });

            modelBuilder.Entity<Xchange>(b =>
            {
                //b.ToTable("Xchanges");
                b.Property(p => p.Id).HasMaxLength(50);
                b.Property(p => p.RetryFor).HasMaxLength(50);
                b.Property(p => p.References); //.IsSeparatorDelimited().HasMaxLength(1024);
                b.Property(p => p.InputHash).IsRequired().HasMaxLength(50);
                b.Property(p => p.InputName).HasMaxLength(200);
                b.Property(p => p.MapperId).HasMaxLength(200);
                b.Property(p => p.HandlerId).HasMaxLength(200);
                b.Property(p => p.HandlerProperties).HasColumnType("jsonb");
                b.Property(p => p.MapperProperties).HasColumnType("jsonb");
                b.Property(p => p.InputContentType).HasMaxLength(200);
                b.Property(p => p.ResponseMessageTypeName).HasMaxLength(500);

                b.HasIndex(i => i.InputHash);
                b.HasIndex(i => i.SubscriptionId);
                b.HasIndex(i => i.StartedOn);
                b.HasIndex(i => i.RetryFor);
            });

            modelBuilder.Entity<OnHoldXchange>(b =>
            {
                b.Property(p => p.Id).ValueGeneratedOnAdd();
                b.Property(p => p.References); //.IsSeparatorDelimited().HasMaxLength(1024);
                b.HasIndex(i => i.SubscriptionId);
            });

            modelBuilder.Entity<XchangeResult>(b =>
            {
                //b.ToTable("XchangeResults");
                b.Property(p => p.Id).HasMaxLength(50);
                b.Property(p => p.OutputHash).HasMaxLength(50);
                b.Property(p => p.OutputName).HasMaxLength(200);
                b.Property(p => p.ResponseHash).HasMaxLength(50);
                b.Property(p => p.ResponseName).HasMaxLength(200);
                b.Property(p => p.ResponseContentType).HasMaxLength(200);
                b.Property(p => p.OutputContentType).HasMaxLength(200);

                b.HasOne<Xchange>().WithOne().HasForeignKey<XchangeResult>(p => p.Id).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<XchangeDelivery>(b =>
            {
                //b.ToTable("XchangeDeliveries");
                b.Property(p => p.Id).HasMaxLength(50);
                b.HasIndex(i => i.DeliveredOn);
                b.HasOne<Xchange>().WithOne().HasForeignKey<XchangeDelivery>(p => p.Id)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<XchangeAggregation>(b =>
            {
                //b.ToTable("XchangeAggregations");
                b.Property(p => p.Id).HasMaxLength(50);
                b.Property(p => p.AggregationXchangeId).IsRequired().HasMaxLength(50);

                b.HasIndex(i => i.AggregationXchangeId);

                b.HasOne<Xchange>().WithOne().HasForeignKey<XchangeAggregation>(p => p.Id)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<XchangePromotedProperties>(b =>
            {
                //b.ToTable("XchangePromotedProperties");
                b.Property(p => p.Id).HasMaxLength(50);
                b.Property(p => p.Properties).HasColumnType("jsonb");
                b.Property(p => p.Hits); //.IsSeparatorDelimited().HasMaxLength(2000);
                b.HasIndex(p => p.PropertiesRaw);
                b.HasOne<Xchange>().WithOne().HasForeignKey<XchangePromotedProperties>(p => p.Id)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Notifier>(b =>
            {
                b.Property(p => p.Id).ValueGeneratedOnAdd();
                b.Property(p => p.Name).HasMaxLength(100).IsRequired();
                b.Property(p => p.HandlerProperties).StoreAsJson();
                b.Property(p => p.HandlerId).HasMaxLength(200).IsUnicode(false);
            });

            modelBuilder.Entity<XchangeNotification>(b =>
            {
                b.Property(p => p.Id).ValueGeneratedOnAdd();
                b.Property(p => p.XchangeId).IsUnicode(false).HasMaxLength(50);
            });

            modelBuilder.Entity<Account>(b =>
            {
                b.ToTable("Accounts");
                b.HasKey(p => p.Id);
                b.Property(p => p.Id).ValueGeneratedOnAdd();
                b.HasIndex(p => p.Email).IsUnique();

                b.Property(p => p.Email).IsUnicode(false).HasMaxLength(200);
                b.Property(p => p.Phone).IsUnicode(false).HasMaxLength(20);
                b.Property(p => p.Password).IsUnicode(false).HasMaxLength(500);
                b.Property(p => p.DisplayName).IsRequired().HasMaxLength(200);

                b.Property(p => p.EmailProvider).HasConversion<byte>();
                b.Property(p => p.LoginMethods).HasConversion<byte>();

                b.HasData(
                    new
                    {
                        Id = 9999,
                        Deleted = false,
                        EmailProvider = EmailProvider.None,
                        LoginMethods = LoginMethod.EmailAndPassword,
                        Email = "admin@infolink.systems",
                        DisplayName = "Admin",
                        CreatedOn = defaultCreatedOn.ToUniversalTime(),
                        Disabled = false,
                        Password = defaultPasswordHash
                    });
            });

            modelBuilder.Entity<RefreshToken>(b =>
            {
                b.ToTable("RefreshTokens");
                b.HasKey(p => p.Id);
                b.HasOne<Account>().WithMany().HasForeignKey(p => p.AccountId).OnDelete(DeleteBehavior.Cascade);

                b.Property(p => p.Id).IsUnicode(false).HasMaxLength(50);
                b.Property(p => p.AccountId);
                b.Property(p => p.LoginMethod).HasConversion<byte>();
            });
        }


        //public static OwnedNavigationBuilder<TOwner, Schedule> BuildSchedule<TOwner>(OwnedNavigationBuilder<TOwner, Schedule> builder)
        //where TOwner : class
        //{
        //    builder.Property(p => p.On).HasConversion<long>();
        //    builder.Property(p => p.Recurrence).HasConversion<byte>();
        //    return builder;
        //}
    }
}