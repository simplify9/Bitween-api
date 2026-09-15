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
            BusProvidersEnabled = false;
            BusProviderMaxInFlight = 16;
            InboundMessagePruneCron = "0 30 3 * * ?";
            ApiCallSubscriptionResponseAcceptedStatusCode = 202;
            StorageProvider = "S3";
            JwtExpiryMinutes = 60;
            BusDefaultQueuePrefetch = 12;
            QueuePrefix = "bitween";
            UseAzureManagedIdentity = false;
        }

        public ushort? BusDefaultQueuePrefetch { get; set; }

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
        public string DocumentPrefix { get; set; }
        public int ServerlessCommandTimeout { get; set; }

        /// <summary>
        /// Runs resident data source providers on this node — brokers and databases alike.
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

        /// <summary>When to forget dedupe keys past their data source's window. Nightly by default.</summary>
        public string InboundMessagePruneCron { get; set; }
        public bool AreXChangeFilesPrivate { get; set; } = false;
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
        public int JwtExpiryMinutes { get; set; }
        public bool ConsumeLegacyEventMessages { get; set; }
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
        /// never itself a setting — it's what protects the table, so it can't live in it. Without
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
        /// Quartz cron expression for <c>ReceiveAttemptCleanupJob</c>. Defaults to daily at 3am —
        /// offset from the scheduler library's own cleanup job (2am) so they don't run at once.
        /// Format: <c>second minute hour dayOfMonth month dayOfWeek</c>
        /// </summary>
        public string ReceiveAttemptCleanupCron { get; set; } = "0 0 3 * * ?";
    }
}