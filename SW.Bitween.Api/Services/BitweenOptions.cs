using System;

namespace SW.Bitween
{
    public class BitweenOptions
    {
        public const string ConfigurationSection = "Bitween";

        public BitweenOptions()
        {
            //   AESEncryptionKey = "BitweenS9SecretKey";
            AdapterPath = "./adapters";
            AdminCredentials = "admin:1234512345";
            DocumentPrefix = "temp30/Bitweendocs";
            ClientIpHeaderName = "X-Real-IP";
            DatabaseType = "MySql";
            AdminDatabaseName = "defaultdb";
            ServerlessCommandTimeout = 300;
            ApiCallSubscriptionResponseAcceptedStatusCode = 202;
            ReceiversDelayInSeconds = 63;
            StorageProvider = "S3";
            JwtExpiryMinutes = 60;
            BusDefaultQueuePrefetch = 12;
            QueuePrefix = "bitween";
            UseAzureManagedIdentity = false;
        }

        public ushort? BusDefaultQueuePrefetch { get; set; }

        public string DatabaseType { get; set; }
        public string AdminDatabaseName { get; set; }
        public string AdapterPath { get; set; }
        public string AdminCredentials { get; set; }
        public string DocumentPrefix { get; set; }
        public string ClientIpHeaderName { get; set; }
        public int ServerlessCommandTimeout { get; set; }
        public bool AreXChangeFilesPrivate { get; set; } = false;
        public int? ApiCallSubscriptionResponseAcceptedStatusCode { get; set; }
        public int? ReceiversDelayInSeconds { get; set; }

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

        public string RabbitMqManagementUrl { get; set; }
        public string RabbitMqManagementUsername { get; set; }
        public string RabbitMqManagementPassword { get; set; }

        /// <summary>
        /// License key for the Rebex POP3 library. When not set, the native Rebex POP3 receiver adapter is not registered.
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