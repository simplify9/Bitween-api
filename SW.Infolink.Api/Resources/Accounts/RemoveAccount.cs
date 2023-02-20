using System.Threading.Tasks;
using SW.Infolink.Domain.Accounts;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Accounts;

[HandlerName("remove")]
public class RemoveAccountModel : ICommandHandler<int, RemoveAccountModel>
{
    private readonly InfolinkDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public RemoveAccountModel(InfolinkDbContext dbContext, RequestContext requestContext)
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