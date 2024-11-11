using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.SubscriptionDraft;

[HandlerName("publish")]
public class Publish : ICommandHandler<int, PublishSubscriptionDraftRequest>
{
    private readonly InfolinkDbContext _dbContext;

    public Publish(InfolinkDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<object> Handle(int key, PublishSubscriptionDraftRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        var draft =
            await _dbContext.Set<Domain.SubscriptionDraft>()
                .Include(i => i.Subscription)
                .FirstOrDefaultAsync(i => i.Id == key);
        if (draft is null)
            throw new SWValidationException("DRAFT_SUBSCRIPTION_WAS_NOT_FOUND",
                $"A draft subscription with id {key}  was not found");
        var trail = new SubscriptionTrail(SubscriptionTrialCode.DraftPublished, draft.Subscription);
        draft.Publish();
        trail.SetAfter(draft.Subscription);
        await _dbContext.SaveChangesAsync();

        return new { Id = key };
    }
}