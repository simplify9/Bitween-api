using System;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain.Accounts
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
        public string DisplayName { get; set; }

        public AccountRole Role { get; private set; }
        public EmailProvider EmailProvider { get; private set; }
        public LoginMethod LoginMethods { get; private set; }

        public bool Disabled { get; private set; }

        public string Password { get; set; }

        public int FailedLoginCount { get; private set; }
        public DateTime? LockoutEnd { get; private set; }

        /// <summary>
        /// Set while the account still has a password nobody chose. Signing in works, but the token
        /// it returns grants nothing until the password is replaced.
        /// </summary>
        /// <remarks>
        /// This exists for one account: the administrator seeded into every installation, whose
        /// password ships in our public repository. On installations where it was never changed,
        /// that published value was full administrative access to anyone who read the repository.
        /// A migration sets this only where the stored password is still that one, so an
        /// installation that changed it years ago notices nothing.
        /// </remarks>
        public bool MustChangePassword { get; private set; }

        /// <summary>
        /// The Microsoft identity — object id and tenant id — this account signs in as, bound on its
        /// first Microsoft sign-in. Matching by address alone let anyone who could get Microsoft to
        /// put that address in a token sign in as the account.
        /// </summary>
        public string MicrosoftIdentity { get; private set; }

        public void BindMicrosoftIdentity(string identity) => MicrosoftIdentity = identity;

        public bool IsLockedOut(DateTime nowUtc) => LockoutEnd.HasValue && LockoutEnd.Value > nowUtc;

        public void RegisterSuccessfulLogin()
        {
            FailedLoginCount = 0;
            LockoutEnd = null;
        }

        /// <summary>
        /// When the member last signed in with a password, Microsoft or the CLI; not when a session
        /// renewed itself. Being on the audited Account, each sign-in is in the audit trail too.
        /// </summary>
        public DateTime? LastSignInOn { get; private set; }

        public void SignedIn() => LastSignInOn = DateTime.UtcNow;

        // Admin action: clear a lockout before it expires.
        public void Unlock()
        {
            FailedLoginCount = 0;
            LockoutEnd = null;
        }


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
            // Whoever set this one chose it, which is the whole requirement.
            MustChangePassword = false;
        }


        public void Update(string name, AccountRole role)
        {
            DisplayName = name;
            Role = role;
        }

        /// <summary>Self-service details. Deliberately cannot touch <see cref="Role"/>.</summary>
        public void UpdateProfile(string name)
        {
            DisplayName = name;
        }

        /// <summary>
        /// Legacy coarse role, kept in step with the account's roles for older API consumers.
        /// Authorization itself reads the role assignments, not this.
        /// </summary>
        public void SetRole(AccountRole role)
        {
            Role = role;
        }

        public void SetDisabled(bool disabled)
        {
            Disabled = disabled;
        }

        public DateTime CreatedOn { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string ModifiedBy { get; set; }
        public bool Deleted { get; set; } = false;
    }
}