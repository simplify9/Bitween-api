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
    private readonly InfolinkDbContext dbContext;
    private readonly RequestContext requestContext;

    public ChangePassword(InfolinkDbContext dbContext, RequestContext requestContext)
    {
        this.dbContext = dbContext;
        this.requestContext = requestContext;
    }

    public async Task<object> Handle(ChangePasswordModel request)
    {
        var accountId = Convert.ToInt32(requestContext.GetNameIdentifier());
        var account = await dbContext.Set<Account>().FindAsync(accountId);

        if (!SecurePasswordHasher.Verify(request.OldPassword, account!.Password))
        {
            throw new SWValidationException("AUTHENTICATION_ERROR",
                "The Password entered does not match the user's password");
        }

        account.SetPassword(request.NewPassword);
        await dbContext.SaveChangesAsync();

        return null;
    }
}