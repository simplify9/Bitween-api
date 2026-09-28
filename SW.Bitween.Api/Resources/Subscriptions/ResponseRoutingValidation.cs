using System.Collections.Generic;
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
/// Only a <see cref="SubscriptionType.Response"/> subscription, because it is the only type whose
/// entry point this is. Every other type has a door of its own — a gateway, a schedule, a
/// partner's call — and feeding it a response runs it through a door it does not have. A bus
/// gateway route was the sharpest case: it ran with the bus skipped, so none of the other routes
/// bound to the same message saw it. <c>ResponseMessageTypeName</c> is the field that actually
/// puts a response on the bus.
/// </para>
/// <para>
/// A target that is already saved is left alone, whatever its type. Internal and ApiCall
/// subscriptions were fed this way before the Response type existed, and refusing them now would
/// make every unrelated edit to those subscriptions fail until someone rewired them.
/// </para>
/// </summary>
internal static class ResponseRoutingValidation
{
    public const string Code = "INVALID_RESPONSE_SUBSCRIPTION";

    /// <summary>Returns the failure message, or null when the destination is allowed.</summary>
    /// <param name="selfId">The subscription being saved; null while it is being created.</param>
    /// <param name="savedTarget">What it points at today; null while it is being created.</param>
    public static async Task<string> CheckDestination(BitweenDbContext dbContext, int? responseSubscriptionId,
        int? selfId = null, int? savedTarget = null)
    {
        if (responseSubscriptionId is null || responseSubscriptionId == savedTarget) return null;

        var type = await dbContext.Set<Subscription>().AsNoTracking()
            .Where(s => s.Id == responseSubscriptionId.Value)
            .Select(s => (SubscriptionType?)s.Type)
            .SingleOrDefaultAsync();

        if (type != SubscriptionType.Response)
            return "A response can only be handed to a response subscription. To reach anything else, " +
                   "publish the response on the bus and let a gateway's routes pick it up.";

        if (selfId != null && await LeadsBackTo(dbContext, responseSubscriptionId.Value, selfId.Value))
            return "That would hand the response round in a loop: the chain from that response " +
                   "subscription leads back to this one.";

        return null;
    }

    /// <summary>Whether following responses on from <paramref name="start"/> ever reaches <paramref name="self"/>.</summary>
    private static async Task<bool> LeadsBackTo(BitweenDbContext dbContext, int start, int self)
    {
        var next = await dbContext.Set<Subscription>().AsNoTracking()
            .Where(s => s.ResponseSubscriptionId != null)
            .Select(s => new { s.Id, Next = s.ResponseSubscriptionId.Value })
            .ToDictionaryAsync(s => s.Id, s => s.Next);

        // A seen-set rather than a depth cap: a loop that already exists further down (saved
        // before this check did) has to end the walk without being mistaken for one through self.
        var seen = new HashSet<int>();
        var at = start;
        while (seen.Add(at))
        {
            if (at == self) return true;
            if (!next.TryGetValue(at, out at)) return false;
        }

        return false;
    }
}
