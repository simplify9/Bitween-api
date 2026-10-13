using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Services;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Subscriptions;

public class SubscriptionBookmarkRequest
{
    public int SubscriptionId { get; set; }
}

public class ForgetSubscriptionBookmark
{
}

/// <summary>Where the subscription's receiver last got to, when it keeps a bookmark; a null value otherwise.</summary>
[HandlerName("bookmark")]
public class Bookmark(BitweenDbContext dbContext, RequestContext requestContext, ReceiverBookmarks bookmarks)
    : IQueryHandler<SubscriptionBookmarkRequest, object>
{
    public async Task<object> Handle(SubscriptionBookmarkRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);
        var receiver = await dbContext.Set<Subscription>().AsNoTracking()
                           .Where(s => s.Id == request.SubscriptionId).Select(s => s.ReceiverId).SingleOrDefaultAsync();
        var state = string.IsNullOrWhiteSpace(receiver) ? null : await bookmarks.GetAsync(request.SubscriptionId, receiver);
        return new { state?.Value, UpdatedOn = state?.UpdatedOn };
    }
}

/// <summary>Forgets the bookmark, so the receiver's next run starts from the beginning.</summary>
[HandlerName("forgetbookmark")]
public class ForgetBookmark(BitweenDbContext dbContext, RequestContext requestContext, ReceiverBookmarks bookmarks)
    : ICommandHandler<int, ForgetSubscriptionBookmark, object>
{
    public async Task<object> Handle(int key, ForgetSubscriptionBookmark request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.Operate);
        if (!await dbContext.Set<Subscription>().AnyAsync(s => s.Id == key)) throw new SWNotFoundException(key.ToString());
        var forgotten = await bookmarks.ForgetAsync(key);
        // What the next run takes changes, so who did it is worth the trail.
        await dbContext.RecordNowAsync(requestContext, Domain.OperatorAction.ForgetBookmark, $"subscription {key}",
            forgotten > 0 ? "the next run starts from the beginning" : "there was no bookmark");
        return new { Forgotten = forgotten > 0 };
    }
}
