using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.SubscriptionDraft;

public class Create : ICommandHandler<CreateSubscriptionDraftRequest>
{
    private readonly InfolinkDbContext _dbContext;

    public Create(InfolinkDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<object> Handle(CreateSubscriptionDraftRequest request)
    {
        var subscription =
            await _dbContext.Set<Subscription>().SingleOrDefaultAsync(i => i.Id == request.SubscriptionId);
        if (subscription is null)
            throw new SWValidationException("SUBSCRIPTION_WAS_NOT_FOUND",
                $"A subscription with id {request.SubscriptionId} was not found");
        var draft = new Domain.SubscriptionDraft(subscription);
        _dbContext.Add(draft);
        await _dbContext.SaveChangesAsync();
        return new
        {
            draft.Id
        };
    }
}