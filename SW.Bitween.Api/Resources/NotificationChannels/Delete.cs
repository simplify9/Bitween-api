using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.NotificationChannels;

public class Delete(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
    : IDeleteHandler<int, object>
{
    public async Task<object> Handle(int key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Notifiers.Delete);

        var channel = await dbContext.FindAsync<NotificationChannel>(key)
                      ?? throw new SWNotFoundException(key.ToString());

        dbContext.Remove(channel);
        await dbContext.SaveChangesAsync();
        await cache.BroadcastRevoke();
        return null;
    }
}
