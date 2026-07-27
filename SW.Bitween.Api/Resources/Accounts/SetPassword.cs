using System;
using System.Threading.Tasks;
using FluentValidation;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

/// <summary>
/// Sets a member's password on their behalf. Bitween has no outbound mail, so there's no
/// self-service reset — without this, anyone who forgets their password is locked out for good.
/// Changing your own password goes through ChangePassword, which asks for the current one.
/// </summary>
[HandlerName("setPassword")]
public class SetPassword : ICommandHandler<int, SetAccountPasswordModel, object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public SetPassword(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(int key, SetAccountPasswordModel request)
    {
        await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Users.Edit);

        if (key == Convert.ToInt32(_requestContext.GetNameIdentifier()))
            throw new SWValidationException("USE_CHANGE_PASSWORD",
                "Use Change password to set your own, so the current one is still required.");

        var account = await _dbContext.Set<Account>().FindAsync(key);
        if (account is null)
            throw new SWValidationException("ACCOUNT_NOT_FOUND", $"No account exists with the id {key}");

        account.SetPassword(request.Password);
        await _dbContext.SaveChangesAsync();

        return null;
    }

    private class Validate : AbstractValidator<SetAccountPasswordModel>
    {
        public Validate()
        {
            RuleFor(i => i.Password).NotEmpty().MinimumLength(8);
        }
    }
}
