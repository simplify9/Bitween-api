using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain.Accounts;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Accounts
{
    public class Create: ICommandHandler<CreateAccountModel>
    {
        private readonly InfolinkDbContext dbContext;

        public Create(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        public async Task<object> Handle(CreateAccountModel request)
        {
            if (string.IsNullOrEmpty(request.Name) || string.IsNullOrEmpty(request.Email) ||
                string.IsNullOrEmpty(request.Password))
                throw new SWValidationException("INVALID_PAYLOAD", "The payload is invalid");
            
            if (await dbContext.Set<Account>().AnyAsync(a => a.Email == request.Email))
                throw new SWValidationException("ACCOUNT_EXISTS", $"Account with email {request.Email} exists");

            var newAccount = new Account(request.Name, request.Email, SecurePasswordHasher.Hash(request.Password));
            dbContext.Add(newAccount);

            await dbContext.SaveChangesAsync();

            return null;

        }
    }
}