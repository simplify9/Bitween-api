using System;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

public class Update : ICommandHandler<int, UpdateAccountModel,object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Update(BitweenDbContext dbContext, RequestContext requestContext)
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