using System;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.Bitween.Resources.Accounts;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.ServiceAccounts;

/// <summary>
/// A new token for a service account. The answer is the only place its value ever appears; after
/// this, Bitween holds only its hash.
/// </summary>
[HandlerName("issueToken")]
public class IssueToken(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<int, CreateServiceTokenModel, object>
{
    public async Task<object> Handle(int key, CreateServiceTokenModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.ServiceAccounts.Create);

        var account = await ServiceAccountStore.Load(dbContext, key);
        // A token can do whatever its account can; issuing one is handing that over.
        await GrantLimits.EnsureCallerOutranks(dbContext, requestContext, key);

        var now = DateTime.UtcNow;
        var active = await dbContext.Set<ServiceToken>()
            .CountAsync(t => t.AccountId == key && t.RevokedOn == null && t.ExpiresOn > now);
        if (active >= ServiceToken.MaxActivePerAccount)
            throw new SWValidationException("TOO_MANY_TOKENS",
                $"'{account.DisplayName}' already has {active} active tokens. Revoke one before issuing another.");

        var days = request.ExpiresInDays ?? ServiceToken.DefaultLifetimeDays;
        var (token, value) = ServiceToken.Issue(key, request.Name.Trim(), now.AddDays(days));
        dbContext.Add(token);
        await dbContext.SaveChangesAsync();

        var issued = ServiceAccountStore.Describe<IssuedServiceTokenModel>(token, now);
        issued.Token = value;
        return issued;
    }

    private class Validate : AbstractValidator<CreateServiceTokenModel>
    {
        public Validate()
        {
            RuleFor(i => i.Name).NotEmpty().MaximumLength(100);
            RuleFor(i => i.ExpiresInDays).InclusiveBetween(1, ServiceToken.MaxLifetimeDays).When(i => i.ExpiresInDays is not null);
        }
    }
}
