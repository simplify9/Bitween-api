using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;

namespace SW.Bitween.Services
{
    /// <summary>
    /// Provides Azure Active Directory token-based authentication for SQL Server connections
    /// using Azure Managed Identity.
    /// </summary>
    public class AzureSqlAuthenticationProvider : SqlAuthenticationProvider
    {
        private readonly TokenCredential _credential;
        private static readonly string[] AzureSqlScopes = new[] { "https://database.windows.net/.default" };

        public AzureSqlAuthenticationProvider(string managedIdentityClientId = null)
        {
            // Check environment variables for Kubernetes Workload Identity
            var clientId = managedIdentityClientId 
                ?? Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")
                ?? Environment.GetEnvironmentVariable("MSI_CLIENT_ID");
            
            if (!string.IsNullOrEmpty(clientId))
            {
                // Use User-Assigned Managed Identity with DefaultAzureCredential for better compatibility
                _credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = clientId
                });
            }
            else
            {
                // Use System-Assigned Managed Identity or DefaultAzureCredential for local dev
                _credential = new DefaultAzureCredential();
            }
        }

        public override async Task<SqlAuthenticationToken> AcquireTokenAsync(SqlAuthenticationParameters parameters)
        {
            var tokenRequestContext = new TokenRequestContext(AzureSqlScopes);
            var tokenResult = await _credential.GetTokenAsync(tokenRequestContext, CancellationToken.None);
            
            return new SqlAuthenticationToken(tokenResult.Token, tokenResult.ExpiresOn);
        }

        public override bool IsSupported(SqlAuthenticationMethod authenticationMethod)
        {
            return authenticationMethod == SqlAuthenticationMethod.ActiveDirectoryManagedIdentity
                || authenticationMethod == SqlAuthenticationMethod.ActiveDirectoryDefault;
        }
    }
}
