using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.NotificationChannels;

public class Get(BitweenDbContext dbContext, RequestContext requestContext, AdapterSecretProperties secrets)
    : IGetHandler<int, object>
{
    public async Task<object> Handle(int key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Notifiers.View);

        var channel = await dbContext.Set<NotificationChannel>().AsNoTracking()
                          .FirstOrDefaultAsync(c => c.Id == key)
                      ?? throw new SWNotFoundException(key.ToString());

        var uses = await NotificationChannelUsage.Find(dbContext);

        return new NotificationChannelGet
        {
            Id = channel.Id,
            Name = channel.Name,
            HandlerId = channel.HandlerId,
            HandlerProperties = await secrets.Mask(channel.HandlerId, channel.HandlerProperties) ?? [],
            UsedBy = uses.GetValueOrDefault(channel.Id) ?? []
        };
    }
}
