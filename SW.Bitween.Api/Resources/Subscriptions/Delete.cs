using Microsoft.EntityFrameworkCore;
using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.PrimitiveTypes;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.Subscriptions
{
    public class Delete : IDeleteHandler<int,object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;


        public Delete(BitweenDbContext dbContext, RequestContext requestContext)
        {
            this._dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(int key)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Subscriptions.Delete);

            await EnsureNothingPointsAtIt(key);

            await _dbContext.DeleteByKeyAsync<Subscription>(key);
            return null;
        }

        /// <summary>
        /// Says what still points at the integration, before the database says it less politely.
        /// </summary>
        /// <remarks>
        /// All four references are <c>RESTRICT</c>, so the delete was already refused — but as a
        /// foreign key violation surfacing as a 500, which tells the operator nothing about which
        /// route or gateway is holding it, and reads like a broken screen rather than a decision.
        /// Exchanges are deliberately not checked: their reference is nullable and history is not
        /// a reason to keep configuration alive.
        /// </remarks>
        private async Task EnsureNothingPointsAtIt(int key)
        {
            var heldBy = new List<string>();

            var routeGateways = await _dbContext.Set<BusGatewayRoute>()
                .Where(r => r.SubscriptionId == key)
                .Select(r => r.BusGateway.Name)
                .Distinct()
                .ToArrayAsync();
            if (routeGateways.Length > 0)
                heldBy.Add($"a route on {Join(routeGateways)}");

            var attachmentGateways = await _dbContext.Set<ApiGatewayPartner>()
                .Where(p => p.SubscriptionId == key)
                .Select(p => p.ApiGateway.Name)
                .Distinct()
                .ToArrayAsync();
            if (attachmentGateways.Length > 0)
                heldBy.Add($"a partner attached to {Join(attachmentGateways)}");

            var fedBy = await _dbContext.Set<Subscription>()
                .Where(s => s.ResponseSubscriptionId == key)
                .Select(s => s.Name)
                .ToArrayAsync();
            if (fedBy.Length > 0)
                heldBy.Add($"the response of {Join(fedBy)}");

            var aggregatedBy = await _dbContext.Set<Subscription>()
                .Where(s => s.AggregationForId == key)
                .Select(s => s.Name)
                .ToArrayAsync();
            if (aggregatedBy.Length > 0)
                heldBy.Add($"the aggregation {Join(aggregatedBy)}");

            if (heldBy.Count == 0)
                return;

            throw new SWValidationException("SUBSCRIPTION_IN_USE",
                $"This integration is still used by {Join(heldBy.ToArray())}. " +
                "Remove that first, or point it at another integration.");
        }

        private static string Join(string[] names) =>
            names.Length == 1
                ? names[0]
                : $"{string.Join(", ", names[..^1])} and {names[^1]}";
    }
}
