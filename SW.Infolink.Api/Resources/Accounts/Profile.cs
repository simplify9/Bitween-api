using System;
using System.Threading.Tasks;
using SW.Infolink.Domain.Accounts;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Accounts;

[HandlerName("profile")]
public class Profile : IQueryHandler
{
    private readonly InfolinkDbContext dbContext;
    private readonly RequestContext requestContext;

    public Profile(InfolinkDbContext dbContext, RequestContext requestContext)
    {
        this.dbContext = dbContext;
        this.requestContext = requestContext;
    }

    public async Task<object> Handle()
    {
        var accountId = Convert.ToInt32(requestContext.GetNameIdentifier());
        var account = await dbContext.Set<Account>().FindAsync(accountId);
        return new AccountModel
        {
            CreatedOn = account!.CreatedOn,
            Email = account.Email,
            Name = account.DisplayName
        };
    }
}