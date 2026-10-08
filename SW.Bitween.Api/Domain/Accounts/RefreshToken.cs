using System;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain.Accounts
{
    public class RefreshToken:BaseEntity<string>, IHasCreationTime
    {
        public RefreshToken(int accountId, LoginMethod loginMethod)
        {
            Id = Guid.NewGuid().ToString("N");
            LoginMethod = loginMethod;
            AccountId = accountId;
        }

        public int AccountId { get; private set; }
        public LoginMethod LoginMethod { get; set; }
        public DateTime CreatedOn { get ; set ; }

        /// <summary>
        /// When a refresh replaced this token. It stays usable for a short grace after that: the
        /// browser only learns the new token from the response, and a response lost to a navigation
        /// would otherwise leave it holding a token that no longer worked — signed out mid-task.
        /// </summary>
        public DateTime? SupersededOn { get; set; }
    }
}