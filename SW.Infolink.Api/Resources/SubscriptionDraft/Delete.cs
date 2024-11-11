using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.EfCoreExtensions;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.SubscriptionDraft;

[HandlerName("Delete")]
public class Delete : ICommandHandler<int, DeleteSubscriptionDraftRequest>
{
    private readonly InfolinkDbContext _dbContext;

    public Delete(InfolinkDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<object> Handle(int key, DeleteSubscriptionDraftRequest request)
    {
        var draft =
            _dbContext.Set<Domain.SubscriptionDraft>().Search("Id", key);

        if (draft is null)
            throw new SWValidationException("DRAFT_SUBSCRIPTION_WAS_NOT_FOUND", $"A draft subscription with id {key}");
        _dbContext.Remove(draft);

        await _dbContext.SaveChangesAsync();

        return new { };
    }
}