using System;
using SW.PrimitiveTypes;

namespace SW.Infolink.Domain.Accounts
{
    public class Account : BaseEntity, IAudited, ISoftDelete
    {
        private Account()
        {
        }

        public Account(string displayName, string email, string password, AccountRole role)
        {
            Password = password;
            DisplayName = displayName;
            Email = email;
            Role = role;
        }

        public string Email { get; private set; }
        public string Phone { get; private set; }
        public string DisplayName { get; set; }

        public AccountRole Role { get; private set; }
        public EmailProvider EmailProvider { get; private set; }
        public LoginMethod LoginMethods { get; private set; }

        public bool Disabled { get; private set; }

        public string Password { get; set; }


        public bool AddEmailLoginMethod(string email, string password)
        {
            Password = password;
            return AddEmailLoginMethod(email, EmailProvider.None);
        }

        private bool AddEmailLoginMethod(string email, EmailProvider provider)
        {
            Email = email;
            EmailProvider = provider;
            return AddLoginMethod(LoginMethod.EmailAndPassword);
        }

        private bool AddLoginMethod(LoginMethod loginMethod)
        {
            if ((LoginMethods & loginMethod) == loginMethod)
                return false;
            LoginMethods |= loginMethod;
            return true;
        }

        public void SetPassword(string password)
        {
            Password = SecurePasswordHasher.Hash(password);
        }


        public DateTime CreatedOn { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string ModifiedBy { get; set; }
        public bool Deleted { get; set; } = false;
    }
}