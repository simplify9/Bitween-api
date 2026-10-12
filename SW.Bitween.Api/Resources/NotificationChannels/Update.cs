using System.Threading.Tasks;
using FluentValidation;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.NotificationChannels;

public class Update(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache,
    AdapterRequirements adapterRequirements)
    : ICommandHandler<int, NotificationChannelUpdate, object>
{
    public async Task<object> Handle(int key, NotificationChannelUpdate request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Notifiers.Edit);

        var channel = await dbContext.FindAsync<NotificationChannel>(key)
                      ?? throw new SWNotFoundException(key.ToString());

        await ChannelValidation.EnsureNameIsFree(dbContext, request.Name, key);
        ChannelValidation.EnsureTransportIsSecure(request.HandlerId, request.HandlerProperties);
        ChannelValidation.EnsureWebhookIsHttps(request.HandlerId, request.HandlerProperties);

        // A hidden secret is put back only while the adapter, and where it connects, stay the same.
        // Otherwise someone who can edit but not see the password could point the channel at their
        // own server and receive it. Re-entering it proves they know it.
        var incoming = request.HandlerProperties ?? [];
        var keep = AdapterSecretProperties.MayKeepStoredSecrets(channel.HandlerId, request.HandlerId,
            channel.HandlerProperties, incoming);
        var properties = AdapterSecretProperties.Merge(channel.HandlerProperties, incoming, keep) ?? [];

        await ChannelValidation.EnsureRequiredPresent(adapterRequirements, request.HandlerId, properties);

        channel.Name = request.Name;
        channel.HandlerId = request.HandlerId;
        channel.HandlerProperties = properties;

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
