using System;
using Microsoft.EntityFrameworkCore;
using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.JsonConverters;

namespace SW.Bitween
{
    public class BitweenDbContext(DbContextOptions options, RequestContext requestContext,
        IPublish publish) : DbContext(options)
    {
        private readonly RequestContext requestContext = requestContext;
        private readonly IPublish publish = publish;

        // Parsed as Unspecified kind, so the .ToUniversalTime() calls at every use site below used
        // to convert using whatever timezone the current machine happened to be in — deterministic
        // on any one developer's machine, but different on every other machine/CI runner, which
        // permanently disagreed with whichever machine last regenerated the migration snapshot.
        protected readonly DateTime defaultCreatedOn = DateTime.SpecifyKind(DateTime.Parse("1/1/2022"), DateTimeKind.Utc);

        // Mtm@dmin!2
        protected readonly string defaultPasswordHash =
            "$SWHASH$V1$10000$VQCi48eitH4Ml5juvBMOFZrMdQwBbhuIQVXe6RR7qJdDF2bJ";

        public const string ConnectionString = "BitweenDb";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Document>(b =>
            {
                b.ToTable("Documents");
                b.Property(p => p.Name).HasMaxLength(100).IsUnicode(false).IsRequired();
                b.Property(p => p.Code).HasMaxLength(50).IsUnicode(false);
                b.Property(p => p.BusMessageTypeName).IsUnicode(false).HasMaxLength(500);
                b.Property(p => p.PromotedProperties).StoreAsJson();
                b.Property(p => p.DisregardsUnfilteredMessages).IsRequired(false);
                b.HasIndex(p => p.Name).IsUnique();
                b.HasIndex(p => p.Code).IsUnique();
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

            modelBuilder.Entity<SubscriptionCategory>(sc =>
            {
                sc.HasKey(i => i.Id);
                sc.Property(i => i.Id).ValueGeneratedOnAdd();
                sc.HasIndex(i => i.Code).IsUnique();
            });
            
            modelBuilder.Entity<WorkGroup>(wg =>
            {
                wg.HasKey(i => i.Id);
                wg.Property(i => i.Id).ValueGeneratedOnAdd();
                wg.Property(p => p.BusMessageName).IsRequired().IsUnicode(false).HasMaxLength(100);
                wg.Property(p => p.Options).StoreAsJson();

            });

            modelBuilder.Entity<ApiGateway>(ag =>
            {
                ag.ToTable("ApiGateways");
                ag.HasKey(i => i.Id);
                ag.Property(i => i.Id).ValueGeneratedOnAdd();
                ag.Property(p => p.Name).IsRequired().HasMaxLength(200);
                ag.Property(p => p.UrlName).IsRequired().HasMaxLength(200);
                ag.HasIndex(p => p.UrlName).IsUnique();
                ag.HasMany(p => p.Partners).WithOne(p => p.ApiGateway).HasForeignKey(p => p.ApiGatewayId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ApiGatewayPartner>(agp =>
            {
                agp.ToTable("ApiGatewayPartners");
                agp.HasKey(p => new { p.ApiGatewayId, p.PartnerId, p.SubscriptionId });
                agp.HasOne(p => p.ApiGateway).WithMany(p => p.Partners).HasForeignKey(p => p.ApiGatewayId)
                    .OnDelete(DeleteBehavior.Restrict);
                agp.HasOne(p => p.Partner).WithMany().HasForeignKey(p => p.PartnerId)
                    .OnDelete(DeleteBehavior.Restrict);
                agp.HasOne(p => p.Subscription).WithMany().HasForeignKey(p => p.SubscriptionId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<BusGateway>(bg =>
            {
                bg.ToTable("BusGateways");
                bg.HasKey(i => i.Id);
                bg.Property(i => i.Id).ValueGeneratedOnAdd();
                bg.Property(p => p.Name).IsRequired().HasMaxLength(200);
                bg.HasOne<Document>().WithMany().HasForeignKey(p => p.DocumentId)
                    .OnDelete(DeleteBehavior.Restrict);
                bg.HasMany(p => p.Routes).WithOne(p => p.BusGateway).HasForeignKey(p => p.BusGatewayId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Nullable on purpose: null keeps meaning "the internal bus", so no existing row
                // changes behaviour and the migration is additive only.
                bg.HasOne(p => p.DataSource).WithMany().HasForeignKey(p => p.DataSourceId)
                    .IsRequired(false).OnDelete(DeleteBehavior.Restrict);
                bg.Property(p => p.Endpoint).HasMaxLength(500).IsUnicode(false);
                bg.Property(p => p.EndpointProperties).StoreAsJson();
            });

            modelBuilder.Entity<DataSource>(ds =>
            {
                ds.ToTable("DataSources");
                ds.HasKey(i => i.Id);
                ds.Property(i => i.Id).ValueGeneratedOnAdd();
                ds.Property(p => p.Name).IsRequired().HasMaxLength(200);
                ds.Property(p => p.AdapterId).IsRequired().HasMaxLength(200).IsUnicode(false);
                ds.Property(p => p.Kind).HasConversion<int>();
                ds.Property(p => p.Properties).StoreAsJson();
                ds.Property(p => p.SecretProperties).StoreAsJson();
                ds.Property(p => p.LastKnownState).HasMaxLength(100).IsUnicode(false);
                ds.Property(p => p.OwnedByNode).HasMaxLength(200).IsUnicode(false);
                ds.HasIndex(p => p.Name).IsUnique();
            });

            modelBuilder.Entity<DataSourceStatement>(st =>
            {
                st.ToTable("DataSourceStatements");
                st.HasKey(i => i.Id);
                st.Property(i => i.Id).ValueGeneratedOnAdd();
                st.Property(p => p.Name).IsRequired().HasMaxLength(200).IsUnicode(false);
                st.Property(p => p.Sql).IsRequired();
                st.Property(p => p.Description).HasMaxLength(1000);

                // Column names, so the database's own identifier limit is the ceiling — 128 is
                // above every engine's (Oracle allows 128, PostgreSQL 63).
                st.Property(p => p.CursorColumn).HasMaxLength(128).IsUnicode(false);
                st.Property(p => p.KeyColumn).HasMaxLength(128).IsUnicode(false);

                // The namespacing fix, enforced by the database rather than by a check someone can
                // forget. Case-insensitivity is handled in the handler, because collation differs
                // per provider and a unique index cannot be relied on to be case-insensitive.
                st.HasIndex(p => new { p.DataSourceId, p.Name }).IsUnique();

                // Cascade, unlike the subscription FK: a statement has no meaning without its
                // connection, so deleting the data source takes its statements with it.
                st.HasOne(p => p.DataSource).WithMany().HasForeignKey(p => p.DataSourceId)
                    .OnDelete(DeleteBehavior.Cascade);

                st.HasOne<WorkGroup>().WithMany().HasForeignKey(p => p.WorkGroupId)
                    .IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<AdapterState>(st =>
            {
                st.ToTable("AdapterStates");

                // Composite key rather than a surrogate: the adapter addresses state by name
                // within its instance, and there is exactly one row per address by definition.
                st.HasKey(p => new { p.AdapterId, p.InstanceKey, p.Name });
                st.Property(p => p.AdapterId).HasMaxLength(200).IsUnicode(false);
                st.Property(p => p.InstanceKey).HasMaxLength(200).IsUnicode(false);
                st.Property(p => p.Name).HasMaxLength(200).IsUnicode(false);
                st.Property(p => p.Value).HasMaxLength(AdapterState.MaxValueLength);
            });

            // Declared here rather than only in the PgSql context: leader election needs this table
            // on every provider Bitween supports, and a node whose database has no cluster_lease
            // cannot fence anything — which means two nodes can consume one queue, silently.
            modelBuilder.Entity<Domain.Cluster.ClusterLease>(cl =>
            {
                cl.ToTable("ClusterLeases");
                cl.HasKey(i => i.Id);
                cl.Property(i => i.Id).HasMaxLength(200).IsUnicode(false);
                cl.Property(i => i.OwnerNode).HasMaxLength(200).IsUnicode(false);
            });

            modelBuilder.Entity<InboundMessage>(im =>
            {
                im.ToTable("InboundMessages");

                // The dedupe key IS the key. A unique constraint the database enforces is the
                // whole mechanism — see the type's remarks.
                im.HasKey(i => i.Id);
                im.Property(i => i.Id).HasMaxLength(400).IsUnicode(false);
                im.Property(i => i.XchangeId).HasMaxLength(50).IsUnicode(false);

                // Pruning scans by age; without this it table-scans a table that only ever grows.
                im.HasIndex(i => i.SeenOn);

                im.HasOne<DataSource>().WithMany().HasForeignKey(i => i.DataSourceId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<BusGatewayRoute>(bgr =>
            {
                bgr.ToTable("BusGatewayRoutes");
                bgr.HasKey(i => i.Id);
                bgr.Property(i => i.Id).ValueGeneratedOnAdd();
                bgr.HasOne(p => p.BusGateway).WithMany(p => p.Routes).HasForeignKey(p => p.BusGatewayId)
                    .OnDelete(DeleteBehavior.Restrict);
                bgr.HasOne(p => p.Subscription).WithMany().HasForeignKey(p => p.SubscriptionId)
                    .OnDelete(DeleteBehavior.Restrict);
                bgr.HasOne(p => p.Partner).WithMany().HasForeignKey(p => p.PartnerId)
                    .IsRequired(false).OnDelete(DeleteBehavior.Restrict);
                bgr.Property(p => p.MatchExpression).HasMatchExpressionConversion();
            });

            modelBuilder.Entity<GlobalAdapterValuesSet>(gav =>
            {
                gav.ToTable("GlobalAdapterValuesSets");
                gav.HasKey(i => i.Id);
                gav.Property(p => p.Id).IsUnicode(false).HasMaxLength(200);
                gav.Property(p => p.Values).StoreAsJson();
            });
            modelBuilder.Entity<Partner>(b =>
            {
                b.ToTable("Partners");
                b.Metadata.SetNavigationAccessMode(PropertyAccessMode.Field);
                b.Property(p => p.Name).IsRequired().IsUnicode(false).HasMaxLength(200);
                b.Property(p => p.AdapterProperties).StoreAsJson();
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

                // Restrict, not cascade: deleting a data source that subscriptions still run
                // through must fail loudly rather than quietly unhooking them.
                b.HasOne<DataSource>().WithMany().HasForeignKey(p => p.DataSourceId).IsRequired(false)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne<Subscription>().WithMany().HasForeignKey(p => p.ResponseSubscriptionId).IsRequired(false)
                    .HasConstraintName("FK_Subscriptions_RespSub").OnDelete(DeleteBehavior.Restrict);
                b.HasOne<Subscription>().WithMany().HasForeignKey(p => p.AggregationForId).IsRequired(false)
                    .HasConstraintName("FK_Subscriptions_AggFor").OnDelete(DeleteBehavior.Restrict);
                b.HasOne(i => i.Category).WithMany().HasForeignKey(i => i.CategoryId);
                b.HasOne(i => i.WorkGroup).WithMany().HasForeignKey(i => i.WorkGroupId);
                b.HasOne(i => i.RetryPolicy).WithMany().HasForeignKey(i => i.RetryPolicyId).IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);
                b.Property(p => p.CustomRetryPolicy).StoreAsJson();
                b.Property(p => p.MatchExpression).HasMatchExpressionConversion();
            });

            modelBuilder.Entity<RetryPolicy>(b =>
            {
                b.ToTable("RetryPolicies");
                b.HasKey(p => p.Id);
                b.Property(p => p.Id).ValueGeneratedOnAdd();
                b.Property(p => p.Name).IsRequired().HasMaxLength(200);
                b.Property(p => p.Groups).StoreAsJson();
                b.Property(p => p.AlertHandlerId).HasMaxLength(200).IsUnicode(false);
                b.Property(p => p.AlertHandlerProperties).StoreAsJson();
            });

            modelBuilder.Entity<DelayedRetry>(b =>
            {
                b.ToTable("DelayedRetries");
                b.HasKey(p => p.Id);
                b.Property(p => p.Id).IsUnicode(false).HasMaxLength(50);
                b.Property(p => p.On);
                b.HasIndex(p => p.On);
            });

            modelBuilder.Entity<ReceiveAttempt>(b =>
            {
                b.ToTable("ReceiveAttempts");
                b.Property(p => p.Id).ValueGeneratedOnAdd();
                b.Property(p => p.ErrorMessage).HasMaxLength(4000);
                b.Property(p => p.ExchangeIds).IsSeparatorDelimited();
                b.HasIndex(p => new { p.SubscriptionId, p.StartedOn });
            });

            modelBuilder.Entity<RetryGroupUsage>(b =>
            {
                b.ToTable("RetryGroupUsages");
                b.HasKey(p => new { p.SubscriptionId, p.GroupId });
                b.Property(p => p.AttemptsUsed);
                b.Property(p => p.LastAttemptOn);
                b.Property(p => p.ExhaustedNotifiedOn);
            });

            modelBuilder.Entity<RetryAlertOverride>(b =>
            {
                b.ToTable("RetryAlertOverrides");
                b.HasKey(p => new { p.SubscriptionId, p.GroupId });
                b.Property(p => p.AlertMode).HasConversion<byte>();
                b.Property(p => p.AlertHandlerId).HasMaxLength(200).IsUnicode(false);
                b.Property(p => p.AlertHandlerProperties).StoreAsJson();
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
                b.Property(p => p.RetryBlockedReason).HasMaxLength(500);
                b.Property(p => p.RetryGroupId);
                b.Property(p => p.AttemptNumber);
                b.HasIndex(p => p.RetryGroupId);


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
                        Email = "admin@Bitween.systems",
                        DisplayName = "Admin",
                        CreatedOn = defaultCreatedOn.ToUniversalTime(),
                        Disabled = false,
                        Password = defaultPasswordHash,
                        Deleted = false,
                        Role = AccountRole.Admin,
                        FailedLoginCount = 0,
                        // True for a fresh installation, whose password is the published default.
                        // Installations that already exist are handled by the migration, which
                        // flags only those still holding that same value.
                        MustChangePassword = true
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

            modelBuilder.Entity<Role>(b =>
            {
                b.ToTable("Roles");
                b.HasKey(p => p.Id);
                b.Property(p => p.Id).ValueGeneratedOnAdd();
                b.HasIndex(p => p.Name).IsUnique();

                b.Property(p => p.Name).IsRequired().HasMaxLength(100);
                b.Property(p => p.Description).HasMaxLength(500);
                b.Property(p => p.Permissions).StoreAsJson();

                b.HasData(SystemRoleSeed());
            });

            modelBuilder.Entity<AccountRoleLink>(b =>
            {
                b.ToTable("AccountRoles");
                b.HasKey(p => new { p.AccountId, p.RoleId });
                b.HasOne<Account>().WithMany().HasForeignKey(p => p.AccountId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne<Role>().WithMany().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Setting>(b =>
            {
                b.ToTable("Settings");
                b.HasKey(p => p.Id);
                // Id is the catalog key, e.g. "Theme.PrimaryColor". Value is left unbounded:
                // it carries anything from a hex color to a license key or a page of blurb.
                b.Property(p => p.Id).IsUnicode(false).HasMaxLength(200);
            });

            // ——— Audit trail ———
            // A table that only ever grows and is only ever appended to. The indexes answer the two
            // questions asked of it: the history of one row, and everything one save changed.
            modelBuilder.Entity<AuditEntry>(b =>
            {
                b.ToTable("AuditEntries");
                b.HasKey(p => p.Id);
                b.Property(p => p.Id).IsUnicode(false).HasMaxLength(32);
                // 36, not 32: the library builds these with Guid.ToString(), which keeps the hyphens.
                b.Property(p => p.CorrelationId).IsUnicode(false).HasMaxLength(36).IsRequired();
                b.Property(p => p.UserId).IsUnicode(false).HasMaxLength(50);
                b.Property(p => p.EntityName).HasMaxLength(200).IsRequired();
                b.Property(p => p.EntityKey).HasMaxLength(200);
                b.Property(p => p.State).IsUnicode(false).HasMaxLength(10).IsRequired();

                b.HasIndex(p => new { p.EntityName, p.EntityKey, p.OccurredOn });
                b.HasIndex(p => p.CorrelationId);
                b.HasIndex(p => p.OccurredOn);
            });

        }

        /// <summary>
        /// The built-in roles. Their grants aren't stored — <see cref="Role.GetEffectivePermissions"/>
        /// derives them from the catalog — so adding a permission never needs a data fix-up here.
        /// </summary>
        protected Role[] SystemRoleSeed() =>
        [
            new Role(Role.AdministratorId, "Administrator",
                    "Full access to everything, including members, roles and settings.")
                { CreatedOn = defaultCreatedOn.ToUniversalTime() },
            new Role(Role.MemberId, "Member",
                    "Runs and configures integrations. Can't manage members, roles or settings.")
                { CreatedOn = defaultCreatedOn.ToUniversalTime() },
            new Role(Role.ViewerId, "Viewer",
                    "Read-only access to integrations, exchanges and configuration.")
                { CreatedOn = defaultCreatedOn.ToUniversalTime() }
        ];

        /// <summary>
        /// Refused, because it would save without auditing. Only <see cref="SaveChangesAsync"/>
        /// captures the trail, and an audit table with a silent hole in it is worse than none —
        /// nobody would know which changes it had missed. Nothing in Bitween calls this today;
        /// this makes sure a future caller finds out immediately rather than quietly.
        /// </summary>
        public override int SaveChanges() => throw new NotSupportedException(
            "Use SaveChangesAsync — the synchronous path would skip the audit trail.");

        /// <summary>
        /// Saves, and records what was saved. The audit rows are written inside the same transaction
        /// as the change they describe, so the trail can never disagree with the data — a save that
        /// rolls back takes its audit rows with it.
        /// </summary>
        /// <remarks>
        /// The transaction is opened only when there is something to audit. Runtime traffic — every
        /// xchange, result and receive attempt — is excluded by <see cref="AuditPolicy"/> and so
        /// still saves in exactly one round trip, unchanged. When a caller has already opened a
        /// transaction of its own, that one is used rather than nested.
        /// </remarks>
        async public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var userId = requestContext.GetNameIdentifier();
            ChangeTracker.ApplyAuditValues(userId);

            var pendingAudit = ChangeTracker.CapturePendingAuditDiffs(userId, AuditPolicy.Options);

            var transaction = pendingAudit.Count > 0 && Database.CurrentTransaction is null
                ? await Database.BeginTransactionAsync(cancellationToken)
                : null;

            int affectedRecords;
            try
            {
                affectedRecords = await base.SaveChangesAsync(cancellationToken);

                if (pendingAudit.Count > 0)
                {
                    // Finalized after the save because that is when a generated primary key exists;
                    // an entry for a newly created row would otherwise record no key at all.
                    foreach (var diff in pendingAudit.FinalizeAuditDiffJson())
                        Add(new AuditEntry(diff));

                    // base, deliberately: re-entering this override would audit the audit rows and
                    // publish every domain event a second time.
                    await base.SaveChangesAsync(cancellationToken);
                }

                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            }
            finally
            {
                if (transaction is not null) await transaction.DisposeAsync();
            }

            // Published after the commit. An event announcing a change that then rolled back would
            // send every consumer after a row that never existed.
            //await ChangeTracker.PublishDomainEvents(publish);
            var entitiesWithEvents = ChangeTracker.Entries<IGeneratesDomainEvents>()
                .Select(e => e.Entity)
                .Where(e => e.Events.Any())
                .ToArray();

            foreach (var entity in entitiesWithEvents)
            {
                var events = entity.Events.ToArray();
                entity.Events.Clear();
                foreach (var domainEvent in events)
                    if (domainEvent is IHasWorkGroup hasWorkGroup)
                        await publish.Publish(hasWorkGroup.GetBusMessageName(),
                            JsonConvert.SerializeObject(new XchangeMessage { Id = hasWorkGroup.Id }));
                    else
                        await publish.Publish(domainEvent.GetType().Name, JsonConvert.SerializeObject(domainEvent));
            }


            return affectedRecords;
        }
    }
}

