using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

[HandlerName("remove")]
public class RemoveAccountModel : ICommandHandler<int, RemoveAccountModel,object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public RemoveAccountModel(BitweenDbContext dbContext, RequestContext requestContext)
    {
        this._dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(int key, RemoveAccountModel request)
    {
        _requestContext.EnsureAccess(AccountRole.Admin);

        var account = await _dbContext.Set<Account>().FindAsync(key);

        if (account is null)
            throw new SWValidationException("ACCOUNT_NOT_FOUND", $"Account with {key} was not found");

        _dbContext.Remove(account);
        await _dbContext.SaveChangesAsync();

        return null;
    }
}