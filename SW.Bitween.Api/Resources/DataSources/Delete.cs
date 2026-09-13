using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.EfCoreExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.DataSources;

public class Delete(BitweenDbContext dbContext, RequestContext requestContext) : IDeleteHandler<int, object>
{
    public async Task<object> Handle(int key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.DataSources.Delete);

        // The database refuses this anyway — the foreign key restricts — but a raw constraint
        // violation tells an operator nothing about which gateway is in the way.
        var gateways = await dbContext.Set<BusGateway>()
            .Where(gateway => gateway.DataSourceId == key)
            .Select(gateway => gateway.Name)
            .Take(5)
            .ToListAsync();

        if (gateways.Count > 0)
            throw new SWException(
                "Cannot delete a data source that still feeds bus gateways: " +
                string.Join(", ", gateways) +
                ". Point them at the internal bus, or delete them, first.");

        // Subscription.DataSourceId restricts too, and was the one left to the database — the
        // commoner case of the two, since a data source is normally read by an integration long
        // before any gateway feeds off it.
        var subscriptions = await dbContext.Set<Subscription>()
            .Where(subscription => subscription.DataSourceId == key)
            .Select(subscription => subscription.Name)
            .Take(5)
            .ToListAsync();

        if (subscriptions.Count > 0)
            throw new SWException(
                "Cannot delete a data source that integrations still read: " +
                string.Join(", ", subscriptions) +
                ". Point them at another data source, or delete them, first.");

        // Dedupe keys cascade with the data source, which is what makes deleting and recreating a
        // data source a genuine reset rather than one that silently suppresses the first messages.
        await dbContext.DeleteByKeyAsync<DataSource>(key);
        await dbContext.SaveChangesAsync();
        return null;
    }
}
