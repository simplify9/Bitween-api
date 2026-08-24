using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Subscriptions;

/// <summary>
/// Guards what a delivery response may be fed into.
/// <para>
/// A bus gateway's route is defined by the message that runs it, but
/// <see cref="Services.XchangeService"/> creates the response exchange against the chosen
/// subscription directly. Picking a route as the response destination therefore runs that one
/// route with the bus skipped entirely: no message published, no route matching, no filter, and
/// none of the other routes bound to the same message. It looks like publishing and is not, so
/// it is refused rather than left as a trap. <c>ResponseMessageTypeName</c> is the field that
/// actually puts a response on the bus.
/// </para>
/// </summary>
internal static class ResponseRoutingValidation
{
    public const string BusGatewayCode = "RESPONSE_SUBSCRIPTION_IS_BUS_GATEWAY";

    /// <summary>Returns the failure message, or null when the destination is allowed.</summary>
    public static async Task<string> CheckDestination(BitweenDbContext dbContext, int? responseSubscriptionId)
    {
        if (responseSubscriptionId is null) return null;

        var type = await dbContext.Set<Subscription>().AsNoTracking()
            .Where(s => s.Id == responseSubscriptionId.Value)
            .Select(s => (SubscriptionType?)s.Type)
            .SingleOrDefaultAsync();

        if (type != SubscriptionType.BusGateway) return null;

        return "A bus gateway route cannot receive a response: it would run with the bus skipped, " +
               "so no other route bound to the same message would see it. Publish the response on " +
               "the bus instead, and let the gateway's routes pick it up.";
    }
}
