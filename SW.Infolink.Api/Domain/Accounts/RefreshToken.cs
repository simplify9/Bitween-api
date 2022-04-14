using System;
using SW.PrimitiveTypes;

namespace SW.Infolink.Domain.Accounts
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
    }
}