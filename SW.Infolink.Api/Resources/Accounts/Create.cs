using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.Infolink.Domain.Accounts;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Accounts
{
    public class Create : ICommandHandler<CreateAccountModel>
    {
        private readonly InfolinkDbContext dbContext;
        private readonly RequestContext _requestContext;

        public Create(InfolinkDbContext dbContext, RequestContext requestContext)
        {
            this.dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(CreateAccountModel request)
        {
            _requestContext.EnsureAccess(AccountRole.Admin);

            if (string.IsNullOrEmpty(request.Name) || string.IsNullOrEmpty(request.Email) ||
                string.IsNullOrEmpty(request.Password))
                throw new SWValidationException("INVALID_PAYLOAD", "The payload is invalid");

            if (await dbContext.Set<Account>().AnyAsync(a => a.Email == request.Email))
                throw new SWValidationException("ACCOUNT_EXISTS", $"Account with email {request.Email} exists");

            var newAccount = new Account(
                request.Name,
                request.Email,
                SecurePasswordHasher.Hash(request.Password),
                (AccountRole)request.Role);
            dbContext.Add(newAccount);

            await dbContext.SaveChangesAsync();

            return null;
        }

        private class Validate : AbstractValidator<CreateAccountModel>
        {
            public Validate()
            {
                RuleFor(i => i.Name).NotEmpty();
                RuleFor(i => i.Email).NotEmpty();
                RuleFor(i => i.Password).NotEmpty();
                RuleFor(i => i.Role).NotEmpty();
            }
        }
    }
}