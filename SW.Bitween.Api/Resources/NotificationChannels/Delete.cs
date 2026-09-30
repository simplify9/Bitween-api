using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.EfCoreExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.NotificationChannels;

public class Delete(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
    : IDeleteHandler<int, object>
{
    /// <remarks>
    /// Refused while anything sends through the channel. Deleting it anyway would leave those places
    /// set to notify and quietly sending nothing, which is worse than being told to move them first.
    /// </remarks>
    public async Task<object> Handle(int key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Notifiers.Delete);

        var uses = (await NotificationChannelUsage.Find(dbContext)).GetValueOrDefault(key);
        if (uses is { Count: > 0 })
            throw new SWValidationException("NOTIFICATION_CHANNEL_IN_USE",
                $"This channel is still used by {string.Join(", ", uses.Select(u => u.Name))}. " +
                "Point them at another channel first.");

        await dbContext.DeleteByKeyAsync<NotificationChannel>(key);
        await cache.BroadcastRevoke();
        return null;
    }
}
