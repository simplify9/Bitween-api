using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.NotificationChannels;

/// <summary>
/// Sends a test message through a channel as it is saved, and says whether it went.
/// </summary>
/// <remarks>
/// Edit rather than view permission: it sends a real message to real people, which is something
/// done to the channel, not a look at it.
/// </remarks>
[HandlerName("test")]
public class Test(BitweenDbContext dbContext, RequestContext requestContext, NotificationChannelSender sender)
    : ICommandHandler<int, NotificationChannelTest, object>
{
    public async Task<object> Handle(int key, NotificationChannelTest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Notifiers.Edit);

        var channel = await dbContext.Set<NotificationChannel>().AsNoTracking()
                          .FirstOrDefaultAsync(c => c.Id == key)
                      ?? throw new SWNotFoundException(key.ToString());

        var error = await sender.SendTest(channel);
        // The reason alone, not a stack trace: it is shown to whoever pressed the button.
        return new NotificationChannelTestResult { Sent = error == null, Error = error?.GetBaseException().Message };
    }
}
