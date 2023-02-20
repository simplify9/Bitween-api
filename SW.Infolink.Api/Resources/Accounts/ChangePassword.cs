using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain.Accounts;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Accounts;

[HandlerName("changePassword")]
public class ChangePassword : ICommandHandler<ChangePasswordModel>
{
    private readonly InfolinkDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public ChangePassword(InfolinkDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(ChangePasswordModel request)
    {
        _requestContext.EnsureAccess(AccountRole.Admin);

        var accountId = Convert.ToInt32(_requestContext.GetNameIdentifier());
        var account = await _dbContext.Set<Account>().FindAsync(accountId);

        if (!SecurePasswordHasher.Verify(request.OldPassword, account!.Password))
        {
            throw new SWValidationException("AUTHENTICATION_ERROR",
                "The Password entered does not match the user's password");
        }

        account.SetPassword(request.NewPassword);
        await _dbContext.SaveChangesAsync();

        return null;
    }
}