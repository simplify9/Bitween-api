using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SW.Infolink.Domain.Accounts;
using SW.Infolink.JsonConverters;

namespace SW.Infolink
{
    public class InfolinkDbContext : DbContext
    {
        private readonly RequestContext requestContext;
        private readonly IPublish publish;

        protected readonly DateTime defaultCreatedOn = DateTime.Parse("1/1/2022");

        // Mtm@dmin!2
        protected readonly string defaultPasswordHash =
            "$SWHASH$V1$10000$VQCi48eitH4Ml5juvBMOFZrMdQwBbhuIQVXe6RR7qJdDF2bJ";

        public const string ConnectionString = "InfolinkDb";


        public InfolinkDbContext(DbContextOptions options, RequestContext requestContext, IPublish publish) :
            base(options)
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
                b.Property(p => p.DisregardsUnfilteredMessages).IsRequired(false);
                b.HasIndex(p => p.Name).IsUnique();
                b.HasIndex(p => p.BusMessageTypeName).IsUnique();

                b.HasMany<Subscription>().WithOne().HasForeignKey(p => p.DocumentId).OnDelete(DeleteBehavior.Restrict);
                b.HasMany<Xchange>().WithOne().HasForeignKey(p => p.DocumentId).OnDelete(DeleteBehavior.Restrict);

                b.HasData(new Document(Document.AggregationDocumentId, "Aggregation Document"));
            });
            modelBuilder.Entity<DocumentTrail>(b =>
            {
                b.HasKey(i => i.Id);
                b.Property(i => i.Id).ValueGeneratedOnAdd();
                b.HasOne(i => i.Document).WithMany().HasForeignKey(i => i.DocumentId);
            });

            modelBuilder.Entity<SubscriptionTrail>(b =>
            {
                b.HasKey(i => i.Id);
                b.Property(i => i.Id).ValueGeneratedOnAdd();
                b.HasOne(i => i.Subscription).WithMany().HasForeignKey(i => i.SubscriptionId);
            });
            modelBuilder.Entity<RunFlagUpdater.RunningResult>(cr =>
            {
                cr.HasNoKey().ToView(null);
                cr.Property(c => c.IsRunning);
            });

            modelBuilder.Entity<Partner>(b =>
            {
                b.ToTable("Partners");
                b.Metadata.SetNavigationAccessMode(PropertyAccessMode.Field);
                b.Property(p => p.Name).IsRequired().IsUnicode(false).HasMaxLength(200);
                b.HasMany(p => p.Subscriptions).WithOne().IsRequired(false).HasForeignKey(p => p.PartnerId)
                    .OnDelete(DeleteBehavior.Restrict);
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

                b.HasOne<Subscription>().WithMany().HasForeignKey(p => p.ResponseSubscriptionId).IsRequired(false)
                    .HasConstraintName("FK_Subscriptions_RespSub").OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Subscription>().WithMany().HasForeignKey(p => p.AggregationForId).IsRequired(false)
                    .HasConstraintName("FK_Subscriptions_AggFor").OnDelete(DeleteBehavior.Restrict);

                b.Property(p => p.MatchExpression).HasConversion(
                    domainObject =>
                        domainObject == null ? null : MatchSpecValueConverter.SerializeMatchSpec(domainObject),
                    dbString => dbString == null ? null : MatchSpecValueConverter.DeserializeMatchSpec(dbString));
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

            modelBuilder.Entity<OnHoldXchange>(b =>
            {
                b.ToTable("OnHoldXchanges");
                b.HasKey(p => p.Id);
                b.Property(p => p.Id).ValueGeneratedOnAdd();
                b.Property(p => p.References).IsSeparatorDelimited().HasMaxLength(1024);
                b.HasIndex(i => i.SubscriptionId);
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

            modelBuilder.Entity<XchangeNotification>(b =>
            {
                b.ToTable("XchangeNotifications");
                b.Property(p => p.Id).ValueGeneratedOnAdd();
                b.Property(p => p.XchangeId).IsUnicode(false).HasMaxLength(50);
            });

            modelBuilder.Entity<XchangeDelivery>(b =>
            {
                b.ToTable("XchangeDeliveries");
                b.Property(p => p.Id).IsUnicode(false).HasMaxLength(50);
                b.HasIndex(i => i.DeliveredOn);
                b.HasOne<Xchange>().WithOne().HasForeignKey<XchangeDelivery>(p => p.Id)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<XchangeAggregation>(b =>
            {
                b.ToTable("XchangeAggregations");
                b.Property(p => p.Id).IsUnicode(false).HasMaxLength(50);
                b.Property(p => p.AggregationXchangeId).IsRequired().IsUnicode(false).HasMaxLength(50);

                b.HasIndex(i => i.AggregationXchangeId);

                b.HasOne<Xchange>().WithOne().HasForeignKey<XchangeAggregation>(p => p.Id)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<XchangePromotedProperties>(b =>
            {
                b.ToTable("XchangePromotedProperties");
                b.Property(p => p.Id).IsUnicode(false).HasMaxLength(50);
                b.Property(p => p.Properties).StoreAsJson();
                //b.Property(p => p.PropertiesRaw);

                b.Property(p => p.Hits).IsSeparatorDelimited().IsUnicode(false).HasMaxLength(2000);

                b.HasIndex(p => p.PropertiesRaw);

                b.HasOne<Xchange>().WithOne().HasForeignKey<XchangePromotedProperties>(p => p.Id)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Notifier>(b =>
            {
                b.ToTable("Notifiers");
                b.Property(p => p.Id).ValueGeneratedOnAdd();
                b.Property(p => p.Name).HasMaxLength(100).IsRequired();
                b.Property(p => p.HandlerProperties).StoreAsJson();
                b.Property(p => p.HandlerId).HasMaxLength(200).IsUnicode(false);
                b.Property(p => p.RunOnSubscriptions).IsSeparatorDelimited();
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
                        EmailProvider = EmailProvider.None,
                        LoginMethods = LoginMethod.EmailAndPassword,
                        Email = "admin@infolink.systems",
                        DisplayName = "Admin",
                        CreatedOn = defaultCreatedOn.ToUniversalTime(),
                        Disabled = false,
                        Password = defaultPasswordHash,
                        Deleted = false,
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

        async public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ChangeTracker.ApplyAuditValues(requestContext.GetNameIdentifier());
            //using var transaction = await Database.BeginTransactionAsync();
            var affectedRecords = await base.SaveChangesAsync(cancellationToken);
            await ChangeTracker.PublishDomainEvents(publish);
            //await transaction.CommitAsync();
            return affectedRecords;
        }
    }
}