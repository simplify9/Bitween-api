using System.Threading.Tasks;
using SW.Infolink.Domain.Accounts;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Accounts;

[HandlerName("remove")]
public class RemoveAccountModel : ICommandHandler<int, RemoveAccountModel>
{
    private readonly InfolinkDbContext dbContext;

    public RemoveAccountModel(InfolinkDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public async Task<object> Handle(int key, RemoveAccountModel request)
    {
        var account = await dbContext.Set<Account>().FindAsync(key);

        if (account is null)
            throw new SWValidationException("ACCOUNT_NOT_FOUND", $"Account with {key} was not found");

        dbContext.Remove(account);
        await dbContext.SaveChangesAsync();

        return null;
    }
}