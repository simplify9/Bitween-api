using System.Threading.Tasks;
using FluentValidation;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.NotificationChannels;

public class Update(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
    : ICommandHandler<int, NotificationChannelUpdate, object>
{
    public async Task<object> Handle(int key, NotificationChannelUpdate request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Notifiers.Edit);

        var channel = await dbContext.FindAsync<NotificationChannel>(key)
                      ?? throw new SWNotFoundException(key.ToString());

        await ChannelValidation.EnsureNameIsFree(dbContext, request.Name, key);
        ChannelValidation.EnsureTransportIsSecure(request.HandlerId, request.HandlerProperties);

        // A masked secret is put back only for the handler it was stored for. Restoring it by key
        // after the handler changed would send the stored password wherever the new handler sends
        // things, without the caller ever having seen it — so a new handler means typing it again.
        var restoreFrom = channel.HandlerId == request.HandlerId ? channel.HandlerProperties : null;

        channel.Name = request.Name;
        channel.HandlerId = request.HandlerId;
        channel.HandlerProperties = AdapterSecretProperties.Merge(restoreFrom, request.HandlerProperties) ?? [];

        await dbContext.SaveChangesAsync();
        await cache.BroadcastRevoke();
        return null;
    }

    private class Validate : AbstractValidator<NotificationChannelUpdate>
    {
        public Validate()
        {
            RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
            RuleFor(c => c.HandlerId).NotEmpty();
        }
    }
}
