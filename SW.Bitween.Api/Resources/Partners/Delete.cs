using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Domain.Gateway;
using SW.PrimitiveTypes;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace SW.Bitween.Resources.Partners
{
    public class Delete(BitweenDbContext dbContext, RequestContext requestContext) : IDeleteHandler<int,object>
    {
        public async Task<object> Handle(int key)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Partners.Delete);

            if (key == Partner.SystemId)
                throw new SWException("System partner can not be deleted.");

            await EnsureNothingPointsAtIt(key);

            // The roles their admin made for their own people go with them; nobody else can hold one.
            dbContext.RemoveRange(await dbContext.Set<Role>().Where(r => r.PartnerId == key).ToListAsync());
            await dbContext.SaveChangesAsync();
            await dbContext.DeleteByKeyAsync<Partner>(key);
            return null;
        }

        /// <summary>
        /// Says what still points at the partner, before the database says it less politely.
        /// </summary>
        /// <remarks>
        /// All three references are <c>RESTRICT</c>, so the delete was already refused - but as a
        /// foreign key violation surfacing as a bare 500, which names a constraint instead of the
        /// integration holding it and reads like a broken screen rather than a decision.
        /// Exchanges are deliberately not checked: they carry a partner id with no foreign key
        /// behind it, and history is not a reason to keep configuration alive.
        /// </remarks>
        private async Task EnsureNothingPointsAtIt(int key)
        {
            var heldBy = new List<string>();

            var subscriptions = await dbContext.Set<Subscription>()
                .Where(s => s.PartnerId == key)
                .Select(s => s.Name)
                .Take(MaxNamed + 1)
                .ToArrayAsync();
            if (subscriptions.Length > 0)
                heldBy.Add($"the integration {Describe(subscriptions)}");

            var apiGateways = await dbContext.Set<ApiGatewayPartner>()
                .Where(p => p.PartnerId == key)
                .Select(p => p.ApiGateway.Name)
                .Distinct()
                .Take(MaxNamed + 1)
                .ToArrayAsync();
            if (apiGateways.Length > 0)
                heldBy.Add($"an attachment on {Describe(apiGateways)}");

            var busGateways = await dbContext.Set<BusGatewayRoute>()
                .Where(r => r.PartnerId == key)
                .Select(r => r.BusGateway.Name)
                .Distinct()
                .Take(MaxNamed + 1)
                .ToArrayAsync();
            if (busGateways.Length > 0)
                heldBy.Add($"a route on {Describe(busGateways)}");

            // Removed accounts don't count: they can't sign in, and a partner they still point at
            // can go, leaving them pointing at nothing.
            var people = await dbContext.Set<Account>()
                .Where(a => a.PartnerId == key && !a.Deleted)
                .Select(a => a.DisplayName)
                .Take(MaxNamed + 1)
                .ToArrayAsync();
            if (people.Length > 0)
                heldBy.Add($"the portal user {Describe(people)}");

            if (heldBy.Count == 0)
                return;

            throw new SWValidationException("PARTNER_IN_USE",
                $"This partner is still used by {Join(heldBy.ToArray())}. " +
                "Remove that first, or point it at another partner.");
        }

        /// <summary>
        /// How many holders the message names before it stops listing them. A partner carrying
        /// forty integrations would otherwise read out all forty into a dialog - longer to
        /// understand than the short version, and forty rows fetched to build it.
        /// </summary>
        private const int MaxNamed = 5;

        /// <summary>Takes one more than it will name, which is how it knows there are others.</summary>
        private static string Describe(string[] names) =>
            names.Length > MaxNamed
                ? $"{string.Join(", ", names[..MaxNamed])} and others"
                : Join(names);

        private static string Join(string[] names) =>
            names.Length == 1
                ? names[0]
                : $"{string.Join(", ", names[..^1])} and {names[^1]}";
    }
}
