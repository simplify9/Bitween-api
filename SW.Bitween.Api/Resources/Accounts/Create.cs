using System.Linq;
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
            await _requestContext.EnsurePermission(dbContext, Model.Permissions.Users.Create);

            if (string.IsNullOrEmpty(request.Name) || string.IsNullOrEmpty(request.Email) ||
                (!_bitweenOptions.DisableEmailPasswordLogin && string.IsNullOrEmpty(request.Password)))
                throw new SWValidationException("INVALID_PAYLOAD", "The payload is invalid");

            if (await dbContext.Set<Account>().AnyAsync(a => a.Email == request.Email))
                throw new SWValidationException("ACCOUNT_EXISTS", $"Account with email {request.Email} exists");

            // Prefer explicit role ids, and fall back to the legacy coarse role only when one was
            // actually sent. It used to be a plain int, so omitting it meant 0 — which is Admin —
            // and adding a member with no roles ticked handed them the run of the instance.
            var roleIds = request.RoleIds is { Count: > 0 }
                ? request.RoleIds.Distinct().ToList()
                : request.Role is null
                    ? []
                    : [BuiltInRoleFor((AccountRole)request.Role)];

            // No password at all when this instance signs in through Microsoft only — the account
            // exists purely to be matched by email.
            var password = _bitweenOptions.DisableEmailPasswordLogin
                ? null
                : SecurePasswordHasher.Hash(request.Password);

            var newAccount = new Account(
                request.Name,
                request.Email,
                password,
                AccountRoles.LegacyRoleFor(roleIds));
            dbContext.Add(newAccount);
            await dbContext.SaveChangesAsync();

            await AccountRoles.Set(dbContext, newAccount.Id, roleIds);
            await dbContext.SaveChangesAsync();

            return newAccount.Id;
        }

        private static int BuiltInRoleFor(AccountRole role) => role switch
        {
            AccountRole.Admin => Role.AdministratorId,
            AccountRole.Member => Role.MemberId,
            _ => Role.ViewerId
        };

        private class Validate : AbstractValidator<CreateAccountModel>
        {
            public Validate(BitweenOptions bitweenOptions)
            {
                RuleFor(i => i.Name).NotEmpty();
                RuleFor(i => i.Email).NotEmpty();
                RuleFor(i => i.Password).Password().When(_ => !bitweenOptions.DisableEmailPasswordLogin);
                // The handler prefers explicit role ids and only falls back to the legacy coarse
                // Role, so demanding Role unconditionally rejected the payload the UI actually
                // sends — adding a member failed outright. Still required when no ids were sent,
                // which is what keeps an account from being created with no roles at all.
                RuleFor(i => i.Role).NotNull().When(i => i.RoleIds is not { Count: > 0 });
            }
        }
    }
}