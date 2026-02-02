using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;

namespace SW.Bitween.Services
{
    /// <summary>
    /// Provides Azure Active Directory token-based authentication for PostgreSQL connections
    /// using Azure Managed Identity.
    /// </summary>
    public class AzurePostgreSqlTokenProvider
    {
        private readonly TokenCredential _credential;
        private static readonly string[] AzurePostgreSqlScopes = new[] { "https://ossrdbms-aad.database.windows.net/.default" };

        public AzurePostgreSqlTokenProvider(string managedIdentityClientId = null)
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

        public async Task<string> GetAccessTokenAsync()
        {
            var tokenRequestContext = new TokenRequestContext(AzurePostgreSqlScopes);
            var tokenResult = await _credential.GetTokenAsync(tokenRequestContext, CancellationToken.None);
            
            return tokenResult.Token;
        }
    }
}
