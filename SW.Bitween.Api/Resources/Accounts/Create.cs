using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts
{
    public class Create : ICommandHandler<CreateAccountModel,object>
    {
        private readonly BitweenDbContext dbContext;
        private readonly RequestContext _requestContext;
        private readonly BitweenOptions _bitweenOptions;

        public Create(BitweenDbContext dbContext, RequestContext requestContext, BitweenOptions bitweenOptions)
        {
            this.dbContext = dbContext;
            _requestContext = requestContext;
            _bitweenOptions = bitweenOptions;
        }

        public async Task<object> Handle(CreateAccountModel request)
        {
            _requestContext.EnsureAccess(AccountRole.Admin);

            if (string.IsNullOrEmpty(request.Name) || string.IsNullOrEmpty(request.Email) ||
                (!_bitweenOptions.DisableEmailPasswordLogin && string.IsNullOrEmpty(request.Password)))
                throw new SWValidationException("INVALID_PAYLOAD", "The payload is invalid");

            if (await dbContext.Set<Account>().AnyAsync(a => a.Email == request.Email))
                throw new SWValidationException("ACCOUNT_EXISTS", $"Account with email {request.Email} exists");

            var password = _bitweenOptions.DisableEmailPasswordLogin
                ? null
                : SecurePasswordHasher.Hash(request.Password);

            var newAccount = new Account(
                request.Name,
                request.Email,
                password,
                (AccountRole)request.Role);
            dbContext.Add(newAccount);

            await dbContext.SaveChangesAsync();

            return null;
        }

        private class Validate : AbstractValidator<CreateAccountModel>
        {
            public Validate(BitweenOptions bitweenOptions)
            {
                RuleFor(i => i.Name).NotEmpty();
                RuleFor(i => i.Email).NotEmpty();
                RuleFor(i => i.Password).NotEmpty().When(_ => !bitweenOptions.DisableEmailPasswordLogin);
                RuleFor(i => i.Role).NotNull();
            }
        }
    }
}