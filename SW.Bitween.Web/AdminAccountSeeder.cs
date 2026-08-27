using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SW.Bitween.Domain.Accounts;

namespace SW.Bitween.Web
{
    public static class AdminAccountSeeder
    {
        private const string DefaultAdminEmail = "admin@bitween.systems";
        private const string DefaultAdminPassword = "Mtm@dmin!2";

        public static void EnsureDefaultAdmin(IHost host)
        {
            using var scope = host.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

            var passwordHash = SecurePasswordHasher.Hash(DefaultAdminPassword);
            var admin = dbContext.Set<Account>().SingleOrDefault(a => a.Email.ToLower() == DefaultAdminEmail);

            if (admin is null)
            {
                admin = new Account("Admin", DefaultAdminEmail, passwordHash, AccountRole.Admin)
                {
                    CreatedOn = DateTime.UtcNow
                };
                dbContext.Add(admin);
            }

            admin.AddEmailLoginMethod(DefaultAdminEmail, passwordHash);
            dbContext.SaveChanges();
        }
    }
}
