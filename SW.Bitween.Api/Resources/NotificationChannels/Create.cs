using System.Threading.Tasks;
using FluentValidation;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.NotificationChannels;

public class Create(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache,
    AdapterRequirements adapterRequirements)
    : ICommandHandler<NotificationChannelCreate, object>
{
    public async Task<object> Handle(NotificationChannelCreate request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Notifiers.Create);
        await ChannelValidation.EnsureNameIsFree(dbContext, request.Name);
        ChannelValidation.EnsureTransportIsSecure(request.HandlerId, request.HandlerProperties);

        // A new channel has nothing stored behind a sentinel, so one that arrives (from a channel
        // copied out of Get, say) is dropped rather than saved as the literal password.
        var properties = AdapterSecretProperties.Merge(null, request.HandlerProperties) ?? [];
        await ChannelValidation.EnsureRequiredPresent(adapterRequirements, request.HandlerId, properties);

        var channel = new NotificationChannel
        {
            Name = request.Name,
            HandlerId = request.HandlerId,
            HandlerProperties = properties
        };

        dbContext.Add(channel);
        await dbContext.SaveChangesAsync();
        await cache.BroadcastRevoke();
        return channel.Id;
    }

    private class Validate : AbstractValidator<NotificationChannelCreate>
    {
        public Validate()
        {
            RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
            RuleFor(c => c.HandlerId).NotEmpty();
        }
    }
}
