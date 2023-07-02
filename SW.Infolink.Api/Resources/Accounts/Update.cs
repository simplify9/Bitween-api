using System;
using System.Threading.Tasks;
using SW.Infolink.Domain.Accounts;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Accounts;

public class Update : ICommandHandler<int, UpdateAccountModel>
{
    private readonly InfolinkDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Update(InfolinkDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(int key, UpdateAccountModel request)
    {
        var loggedInUserId = Convert.ToInt32(_requestContext.GetNameIdentifier());

        if (key != loggedInUserId)
            _requestContext.EnsureAccess(AccountRole.Admin);


        
        var account = await _dbContext.Set<Account>().FindAsync(key);
        if (account is null)
            throw new SWValidationException("ACCOUNT_NOT_FOUND", $"No account exists with the id {key}");


        account.Update(request.Name, (AccountRole)request.Role);
        await _dbContext.SaveChangesAsync();

        return null;
    }
}