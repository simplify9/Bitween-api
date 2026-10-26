using System;

namespace SW.Bitween
{
    public class BitweenOptions
    {
        public const string ConfigurationSection = "Bitween";

        public BitweenOptions()
        {
            //   AESEncryptionKey = "BitweenS9SecretKey";
            AdapterPath = "adapters";
            DocumentPrefix = "temp30/Bitweendocs";
            DatabaseType = "MySql";
            AdminDatabaseName = "defaultdb";
            ServerlessCommandTimeout = 300;
            NativeAdapterTimeout = 900;
            BusProvidersEnabled = false;
            BusProviderMaxInFlight = 16;
            BusProviderUnhealthyRestartSeconds = 120;
            InboundMessagePruneCron = "0 30 3 * * ?";
            ApiCallSubscriptionResponseAcceptedStatusCode = 202;
            StorageProvider = "S3";
            JwtExpiryMinutes = 60;
            PartnerKeyHeader = PartnerKeyHeaders.Default;
            BusDefaultQueuePrefetch = 12;
            BusPublisherConfirms = true;
            QueuePrefix = "bitween";
            UseAzureManagedIdentity = false;
        }

        public ushort? BusDefaultQueuePrefetch { get; set; }

        /// <summary>
        /// How many messages of a lane run at once on a node when nothing more specific says: the
        /// ungrouped lane, and Bitween's other bus consumers. A work group sets its own lane's.
        /// Unset, it is the default prefetch, which is what the docs always said a lane did.
        /// </summary>
        public ushort? BusDefaultConcurrency { get; set; }

        /// <summary><see cref="BusDefaultConcurrency"/>, or the default prefetch when it is unset.</summary>
        public ushort ResolvedBusDefaultConcurrency =>
            BusDefaultConcurrency ?? BusDefaultQueuePrefetch ?? 12;

        /// <summary>
        /// Whether a publish on Bitween's own bus waits for the broker to confirm it, and fails if
        /// it doesn't. On, SW.Bus's default since 10.1: the outbox removes a row only once its message
        /// is on the broker. Off is how 10.0 published, for a deployment that can't afford the wait.
        /// </summary>
        public bool BusPublisherConfirms { get; set; }

        public string DatabaseType { get; set; }
        public string AdminDatabaseName { get; set; }

        /// <summary>
        /// Cloud-storage key prefix the serverless runner downloads custom adapter packages from
        /// (<c>{AdapterPath}/{adapterId}</c>). Passed to <c>ServerlessOptions.AdapterRemotePath</c>.
        /// </summary>
        public string AdapterPath { get; set; }

        /// <summary>
        /// Serves the API description and the Swagger interface. Off unless switched on.
        /// </summary>
        /// <remarks>
        /// A penetration test read all 94 endpoints and their request and response shapes off a
        /// deployed instance without signing in, and used the repository link it found alongside
        /// them to reach our published source. Useful while building, an inventory for a stranger
        /// anywhere else.
        /// <para>
        /// A setting of its own rather than a Development-only behaviour: the deployment chart
        /// defaults ASPNETCORE_ENVIRONMENT to Development, so keying this to the environment would
        /// have left it on for exactly the deployments nobody had configured.
        /// </para>
        /// </remarks>
        public bool ExposeApiDocs { get; set; }

        /// <summary>
        /// Refuse HTTP adapter calls to private and loopback addresses (10/8, 172.16/12,
        /// 192.168/16, 127/8, fc00::/7 and the like). Off by default: Bitween is self-hosted, and
        /// calling systems inside the customer's own network is a normal integration. Link-local
        /// addresses, where the cloud metadata endpoints live, are refused either way.
        /// </summary>
        public bool BlockPrivateNetworkAddresses { get; set; }

        /// <summary>
        /// Comma-separated addresses or CIDR ranges of the proxies whose X-Forwarded-For is
        /// believed. Empty trusts the nearest proxy whatever its address, which is right behind an
        /// ingress whose address isn't known ahead of time; set it when clients can also reach the
        /// service directly, or they can choose the address they are rate-limited as.
        /// </summary>
        public string TrustedProxies { get; set; }

        /// <summary>
        /// How long a receiver run may hold its subscription's running flag before another run may
        /// take it over. A run killed with its process never clears the flag; this is what lets the
        /// subscription run again. Longer than any real run, so a slow one is not run twice.
        /// </summary>
        public int StaleRunAfterMinutes { get; set; } = 120;

        /// <summary>
        /// The longest a synchronous gateway call is held open waiting for its exchange's result,
        /// whatever wait the caller asks for. After it the caller gets 202 with the exchange id.
        /// </summary>
        public int MaxResponseWaitSeconds { get; set; } = 300;

        /// <summary>
        /// The longest a retry chain may grow, whatever its policy allows. A policy with no total
        /// limit used to retry a failure that never recovers for ever.
        /// </summary>
        public int MaxRetryChainDepth { get; set; } = 100;

        /// <summary>
        /// The longest an adapter's "try again after" may push back a retry its policy allowed, in
        /// seconds: six hours. A longer wait is cut to this, so no partner can park retries for days.
        /// 0 ignores what adapters ask, and retries on the policy's schedule as before.
        /// </summary>
        public int MaxRetryAfterSeconds { get; set; } = 21600;

        /// <summary>
        /// The largest input, in bytes, an exchange's created message carries beside its id, so the
        /// worker need not read it back from storage: 256 KB. Larger ones are read from storage. Every
        /// file is written to storage either way. 0 sends none.
        /// </summary>
        public int MessagePayloadMaxBytes { get; set; } = 262144;

        /// <summary>
        /// Whether the built-in mappers keep their compiled Scriban templates and mapping rules, by
        /// their text, instead of compiling them on every exchange: on. Off compiles them per call, as
        /// before. Read at startup.
        /// </summary>
        public bool CacheCompiledTemplates { get; set; } = true;

        /// <summary>
        /// Whether an exchange's payload is parsed once per parse profile and the tree handed from step
        /// to step (the gateway's schema check and promoted properties; the worker's schema check,
        /// filter, enrichment and built-in mappers), instead of each step parsing it again: on. Off
        /// returns every step to its own parse, as before.
        /// </summary>
        public bool SharedPayloadParse { get; set; } = true;

        /// <summary>
        /// After a notifier sends for a subscription and outcome, further notifications for the same
        /// pair within this many minutes are recorded as suppressed rather than sent, and the next
        /// one sent says how many there were. An outage no longer means one email per failure.
        /// Zero sends every one.
        /// </summary>
        public int NotifierQuietMinutes { get; set; } = 5;
        /// <summary>
        /// Where new exchange files are written in storage. The bucket's lifecycle rule for this prefix
        /// decides how long they're kept (<c>temp30/…</c> is 30 days). Each exchange records the prefix
        /// it was written under, so a change only affects exchanges created after it.
        /// </summary>
        public string DocumentPrefix { get; set; }

        /// <summary>
        /// The prefix exchange files were written under before each exchange recorded its own. Remembered
        /// once, the first time this version starts, so those files stay reachable after
        /// <see cref="DocumentPrefix"/> changes. Kept in the Settings table but not a setting anyone edits.
        /// </summary>
        public string LegacyDocumentPrefix { get; set; }

        /// <summary>
        /// The address partners reach this instance on, e.g. <c>https://bitween.example.com</c>. Not a
        /// setting: the chart fills it in from its own host name. File links for readers without a login
        /// are built on it. Without it a request's links use the address it came in on, and aggregation
        /// roll-ups use this instance's own address, which only adapters running in Bitween can open.
        /// </summary>
        public string PublicUrl { get; set; }

        /// <summary>
        /// The mail server every Email notification channel sends through. One instance for the life
        /// of the app: Settings changes its properties in place, and the Email adapter reads them on
        /// each send, so it must never be replaced.
        /// </summary>
        public SW.Bitween.NativeAdapters.ChannelMessages.MailServerOptions MailServer { get; } = new();

        /// <summary>
        /// The AI provider AI features call. One instance for the life of the app, for the same
        /// reason as <see cref="MailServer"/>: Settings changes it in place.
        /// </summary>
        public SW.Bitween.Services.Ai.AiOptions Ai { get; } = new();

        /// <summary>Days an exchange is kept before the retention job removes it. 0 keeps exchanges for ever.</summary>
        public int ExchangeRetentionDays { get; set; }

        /// <summary>Whether the retention job copies each exchange, files included, to the archive before deleting it.</summary>
        public bool ArchiveExchanges { get; set; } = true;

        /// <summary>
        /// Quartz cron expression for <c>ExchangeRetentionJob</c>. Daily at 4am by default, after the other
        /// nightly clean-ups. Format: <c>second minute hour dayOfMonth month dayOfWeek</c>
        /// </summary>
        public string ExchangeRetentionCron { get; set; } = "0 0 4 * * ?";

        public int ServerlessCommandTimeout { get; set; }

        /// <summary>
        /// Seconds one call to a built-in adapter may take before it fails as a timeout, a retry policy's to
        /// retry. Built-in adapters run in Bitween's own process, so a call that never returns (a server that
        /// accepts a connection and says nothing) held its place in the processing queue for good, and enough
        /// of them stopped all processing. 0 for no limit.
        /// </summary>
        public int NativeAdapterTimeout { get; set; }

        /// <summary>
        /// Write SQL parameter values into EF's logs. They are passwords, partner keys and payloads, so this is
        /// off unless set, in every environment: a developer turns it on for their own machine
        /// (launchSettings.json), and a staging that runs as Development no longer logs them.
        /// </summary>
        public bool LogSqlParameterValues { get; set; }

        /// <summary>
        /// Failures in a row (timeouts, refused or dropped connections, 429, 5xx but 501) after which a
        /// built-in adapter's calls to that destination fail at once, as a 503, for
        /// <see cref="CircuitBreakerOpenSeconds"/>. A partner that stopped answering held a place in the
        /// processing queue for each delivery's whole timeout, and flows to healthy partners waited behind
        /// it. Counted per destination and per replica. 0 switches it off.
        /// </summary>
        public int CircuitBreakerFailures { get; set; } = 5;

        /// <summary>The failures must fall within this many seconds to open the circuit.</summary>
        public int CircuitBreakerWindowSeconds { get; set; } = 60;

        /// <summary>Seconds an open circuit fails calls at once before it lets one through to try.</summary>
        public int CircuitBreakerOpenSeconds { get; set; } = 60;

        /// <summary>
        /// HTTP deliveries get a circuit per endpoint (host and path) instead of per host. Off keeps the
        /// per-host circuit every deployment has had, under which one failing endpoint stopped calls to
        /// every other endpoint on the same host.
        /// </summary>
        public bool CircuitBreakerPerEndpoint { get; set; }

        /// <summary>
        /// Lets the adapter editor build drafts that name packages in requirements.txt or
        /// package.json: the server then downloads them from PyPI and npm, or from the mirrors
        /// PIP_INDEX_URL and NPM_CONFIG_REGISTRY name. Off by default, since it needs the server to
        /// reach a package index. Only plain names and versions are accepted, wheels only for Python
        /// and no install scripts for Node, so nothing a package brings runs while it is built.
        /// </summary>
        public bool AdapterEditorDependencies { get; set; }

        /// <summary>
        /// Memory, in megabytes, a draft may use while the adapter editor checks or tries it on the
        /// server; past it the run is stopped and says so. 0 for no limit.
        /// </summary>
        public int AdapterEditorMemoryMb { get; set; } = 256;

        /// <summary>
        /// Cores a draft may keep busy while the editor checks or tries it, held for a few seconds
        /// before it is stopped. 0 for no limit.
        /// </summary>
        public double AdapterEditorCpuCores { get; set; } = 1;

        /// <summary>
        /// This deployment's release, e.g. 10.0.4 - set by a pipeline that knows it. Adapters may
        /// declare the lowest Bitween they work with; see <see cref="BitweenInfo"/>.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Runs resident data source providers on this node - brokers and databases alike.
        ///
        /// Named for brokers because it predates database sources; renaming it would break every
        /// deployment that already sets it, so operator-facing messages say what it gates rather
        /// than repeating the name.
        ///
        /// Safe on every node: each data source is owned through a lease, so exactly one node
        /// consumes it and the rest stand by. Still opt-in, because it opens outbound connections
        /// to third-party systems and that should be a decision rather than a default.
        /// </summary>
        public bool BusProvidersEnabled { get; set; }

        /// <summary>Unacknowledged messages one bus adapter may have in flight with the host.</summary>
        public int BusProviderMaxInFlight { get; set; }

        /// <summary>
        /// Seconds a leased data source's adapter may report itself disconnected, or be quarantined
        /// or missing, before the supervisor restarts it. 0 leaves it as it is.
        /// </summary>
        /// <remarks>
        /// The RabbitMQ adapter leaves reconnecting to the supervisor, and the supervisor only ever
        /// looked at whether the configuration changed. In the lab a broker that was down for 50
        /// minutes left its data source dead for two hours after it came back, until a pod restart.
        /// </remarks>
        public int BusProviderUnhealthyRestartSeconds { get; set; }

        /// <summary>When to forget dedupe keys past their data source's window. Nightly by default.</summary>
        public string InboundMessagePruneCron { get; set; }
        public int? ApiCallSubscriptionResponseAcceptedStatusCode { get; set; }

        public string StorageProvider { get; set; }

        // public string AESEncryptionKey { get; set; }
        public string MsalClientId { get; set; }
        public string MsalRedirectUri { get; set; }

        public string MsalTenantId { get; set; }

        /// <summary>
        /// When true, disables email/password login and account creation with a password.
        /// Only Microsoft (MSAL) login is allowed: the login page hides the email/password
        /// form, new accounts are created without a password, and the Login handler rejects
        /// any email/password login attempt outright.
        /// </summary>
        public bool DisableEmailPasswordLogin { get; set; }

        /// <summary>
        /// The OpenID Connect provider's issuer address, as <c>https://id.example.com/realms/main</c>.
        /// With <see cref="OidcClientId"/>, turns on single sign-on with it. Off when empty.
        /// </summary>
        public string OidcAuthority { get; set; }
        public string OidcClientId { get; set; }

        /// <summary>For a confidential client; a public one with PKCE needs none.</summary>
        public string OidcClientSecret { get; set; }

        /// <summary>Where the provider sends the browser back; this Bitween's <c>/oidc-callback</c> when empty.</summary>
        public string OidcRedirectUri { get; set; }

        /// <summary>Space-separated; <c>openid email profile</c> when empty.</summary>
        public string OidcScopes { get; set; }

        /// <summary>The sign-in button's label, as "Sign in with {name}".</summary>
        public string OidcDisplayName { get; set; }
        public int JwtExpiryMinutes { get; set; }

        /// <summary>
        /// The header partners send their API key in, system-wide. A gateway can name its own, and
        /// <c>partnerkey</c> keeps working either way. Read per request.
        /// </summary>
        public string PartnerKeyHeader { get; set; }
        public string QueuePrefix { get; set; }

        /// <summary>
        /// Enable Azure Managed Identity for database authentication.
        /// When enabled, access tokens are automatically acquired using managed identity.
        /// Works with Azure SQL Database and PostgreSQL Flexible Server.
        /// </summary>
        public bool UseAzureManagedIdentity { get; set; }

        /// <summary>
        /// Optional: Specify a User-Assigned Managed Identity Client ID.
        /// Leave empty to use System-Assigned Managed Identity.
        /// Also checks environment variables: AZURE_CLIENT_ID, MSI_CLIENT_ID for Kubernetes Workload Identity.
        /// </summary>
        public string AzureManagedIdentityClientId { get; set; }

        /// <summary>
        /// Passphrase used to encrypt secret settings before they're stored. Environment-only and
        /// never itself a setting - it's what protects the table, so it can't live in it. Without
        /// it, secret settings are neither imported nor editable and keep coming from configuration.
        /// Rotating it makes anything already stored unreadable.
        /// </summary>
        public string SettingsEncryptionKey { get; set; }

        public string RabbitMqManagementUrl { get; set; }
        public string RabbitMqManagementUsername { get; set; }
        public string RabbitMqManagementPassword { get; set; }

        /// <summary>
        /// License key for the Rebex library the native POP3 and FTP adapters are built on. Those
        /// adapters are always registered, so a key stored in Settings takes effect without a
        /// restart; while no key is set they're kept out of the adapter pickers instead.
        /// </summary>
        public string RebexLicenseKey { get; set; }

        /// <summary>Simplify9's registry, which every Bitween uses unless Settings names another.</summary>
        public const string OfficialRegistryUrl = "https://registry.bitween.io";

        /// <summary>
        /// The public keys Simplify9's registry countersigns with. A new key is added here before the
        /// registry starts using it, and an old one stays while connectors it signed are still offered.
        /// </summary>
        public const string OfficialRegistryKeys = "MCowBQYDK2VwAyEAHjZoLJQT1V4Q47w3VjK66vgg/vg0CC48XugEIHqyf8Y=";

        /// <summary>
        /// Whether the Adapters page's Marketplace tab is on. On by default: browsing reaches the
        /// registry only when someone opens the tab, and installing is always someone's choice. An
        /// administrator turns it off under Settings, Marketplace, for a site that must not reach out.
        /// </summary>
        public bool RegistryEnabled { get; set; } = true;

        /// <summary>The Bitween Registry the Marketplace tab browses and installs from: Simplify9's unless changed.</summary>
        public string RegistryUrl { get; set; } = OfficialRegistryUrl;

        /// <summary>
        /// The registry's Ed25519 public keys, base64 or PEM, separated by commas or new lines. A
        /// version installs only when the registry's countersignature verifies with one of them, so a
        /// changed address alone, or a server pretending to be the registry, can't install anything.
        /// </summary>
        public string RegistryTrustedKeys { get; set; } = OfficialRegistryKeys;

        /// <summary>Whether connectors from Community publishers, which Simplify9 hasn't vetted, may be installed.</summary>
        public bool RegistryAllowCommunity { get; set; }

        /// <summary>
        /// What the registry calls this Bitween once it is registered with a code from Settings, Marketplace.
        /// Registering is needed only for private listings, paid connectors and usage reports.
        /// </summary>
        public string RegistryInstanceId { get; set; }

        /// <summary>The instance token the registry gave at registration, sent with every call to it. A secret.</summary>
        public string RegistryInstanceToken { get; set; }

        /// <summary>
        /// Whether a daily usage report goes to the registry: per connector installed from it, how many
        /// subscriptions use it, calls and failures; nothing else. Off unless turned on.
        /// </summary>
        public bool RegistryUsageReports { get; set; }

        /// <summary>
        /// Allowed CORS origins for credential-bearing requests (cookies).
        /// When set, enables AllowCredentials() on the CORS policy.
        /// Example: ["https://localhost:3000", "https://slim-dev.starlinks-me.com"]
        /// </summary>
        public string[] CorsOrigins { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Quartz cron expression that controls how often <c>RetryJob</c> polls for due
        /// auto-retry records. Defaults to every minute.
        /// Format: <c>second minute hour dayOfMonth month dayOfWeek</c>
        /// </summary>
        public string RetryJobCron { get; set; } = "0 * * * * ?";

        /// <summary>
        /// How many days to keep <c>ReceiveAttempt</c> rows before <c>ReceiveAttemptCleanupJob</c>
        /// deletes them. Matches the scheduler library's own <c>JobExecution</c> retention default.
        /// </summary>
        public int ReceiveAttemptRetentionDays { get; set; } = 30;

        /// <summary>
        /// Quartz cron expression for <c>ReceiveAttemptCleanupJob</c>. Defaults to daily at 3am -
        /// offset from the scheduler library's own cleanup job (2am) so they don't run at once.
        /// Format: <c>second minute hour dayOfMonth month dayOfWeek</c>
        /// </summary>
        public string ReceiveAttemptCleanupCron { get; set; } = "0 0 3 * * ?";

        /// <summary>
        /// How many days to keep audit entries before the daily clean-up deletes them. 0, the default, keeps
        /// them for good, as before: the trail is evidence, and only an administrator decides it can go.
        /// </summary>
        public int AuditRetentionDays { get; set; }

        /// <summary>How many days of hourly metrics to keep. A year and a bit, for "same week last year".</summary>
        public int MetricsRetentionDays { get; set; } = 400;

        /// <summary>
        /// How many days of exchange history to fold into the metrics once, after the upgrade that adds
        /// them, a few hours at a time. 0 skips it.
        /// </summary>
        public int MetricsBackfillDays { get; set; } = 30;

        /// <summary>When to delete metrics and broker snapshots past their keeping. Nightly.</summary>
        public string MetricsPruneCron { get; set; } = "0 15 3 * * ?";

        /// <summary>How often to record the broker's queue totals for the backlog trend. Every five minutes.</summary>
        public string QueueSnapshotCron { get; set; } = "0 0/5 * * * ?";

        /// <summary>
        /// How often to look for subscriptions that expect traffic and have gone quiet. Every five
        /// minutes, which is also the shortest gap a subscription can expect.
        /// </summary>
        public string QuietSubscriptionsCron { get; set; } = "0 0/5 * * * ?";
    }
}