using SW.EfCoreExtensions;
using SW.Bitween.Domain;
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

            await dbContext.DeleteByKeyAsync<Partner>(key);
            return null;
        }

        /// <summary>
        /// Says what still points at the partner, before the database says it less politely.
        /// </summary>
        /// <remarks>
        /// All three references are <c>RESTRICT</c>, so the delete was already refused — but as a
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
                .ToArrayAsync();
            if (subscriptions.Length > 0)
                heldBy.Add($"the integration {Join(subscriptions)}");

            var apiGateways = await dbContext.Set<ApiGatewayPartner>()
                .Where(p => p.PartnerId == key)
                .Select(p => p.ApiGateway.Name)
                .Distinct()
                .ToArrayAsync();
            if (apiGateways.Length > 0)
                heldBy.Add($"an attachment on {Join(apiGateways)}");

            var busGateways = await dbContext.Set<BusGatewayRoute>()
                .Where(r => r.PartnerId == key)
                .Select(r => r.BusGateway.Name)
                .Distinct()
                .ToArrayAsync();
            if (busGateways.Length > 0)
                heldBy.Add($"a route on {Join(busGateways)}");

            if (heldBy.Count == 0)
                return;

            throw new SWValidationException("PARTNER_IN_USE",
                $"This partner is still used by {Join(heldBy.ToArray())}. " +
                "Remove that first, or point it at another partner.");
        }

        private static string Join(string[] names) =>
            names.Length == 1
                ? names[0]
                : $"{string.Join(", ", names[..^1])} and {names[^1]}";
    }
}
