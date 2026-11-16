using System;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain.Accounts
{
    public class RefreshToken:BaseEntity<string>, IHasCreationTime
    {
        public RefreshToken(int accountId, LoginMethod loginMethod, string clientId = null, string clientName = null)
        {
            Id = Guid.NewGuid().ToString("N");
            LoginMethod = loginMethod;
            AccountId = accountId;
            ClientId = clientId;
            ClientName = clientName;
        }

        public int AccountId { get; private set; }
        public LoginMethod LoginMethod { get; set; }
        public DateTime CreatedOn { get ; set ; }

        /// <summary>
        /// When a refresh replaced this token. It stays usable for a short grace after that: the
        /// browser only learns the new token from the response, and a response lost to a navigation
        /// would otherwise leave it holding a token that no longer worked - signed out mid-task.
        /// </summary>
        public DateTime? SupersededOn { get; set; }

        /// <summary>
        /// The app an OAuth sign-in was for, such as an AI app connected at /mcp; null for the browser
        /// and the CLI. A token renews only for the app it was issued to, and an app's never renews a
        /// browser session: what an app was allowed stays what it was allowed.
        /// </summary>
        public string ClientId { get; private set; }

        /// <summary>
        /// The app's own name for itself, as its member approved it, for the list of connected apps;
        /// or CLI for a session the CLI signed in. Null for the browser. The audit trail shows it.
        /// </summary>
        public string ClientName { get; private set; }
    }
}