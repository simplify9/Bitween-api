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
            AdminCredentials = "admin:1234512345";
            DocumentPrefix = "temp30/Bitweendocs";
            DatabaseType = "MySql";
            AdminDatabaseName = "defaultdb";
            ServerlessCommandTimeout = 300;
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
        public string AdminCredentials { get; set; }
        public string DocumentPrefix { get; set; }
        public int ServerlessCommandTimeout { get; set; }
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
    }
}