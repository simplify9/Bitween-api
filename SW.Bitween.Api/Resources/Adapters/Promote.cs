using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Adapters;

public class AdapterPromoteRequest
{
    public string AdapterId { get; set; }
    public string Version { get; set; }
}

/// <summary>
/// Makes a published version the one that runs wherever nothing is pinned — rolling back is
/// promoting an older one. What serverless promote does, from Bitween, and in the audit trail.
/// </summary>
[HandlerName("promote")]
public class Promote(BitweenDbContext dbContext, RequestContext requestContext, AdapterWorkshop workshop)
    : ICommandHandler<AdapterPromoteRequest, object>
{
    public async Task<object> Handle(AdapterPromoteRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.AdapterSource.Operate);
        var adapterId = request.AdapterId?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(adapterId) || NativeAdapterDiscoveryService.IsNative(adapterId))
            throw new SWValidationException("AdapterId", "Only a published adapter has versions to promote.");

        await workshop.PromoteAsync(adapterId, request.Version);
        dbContext.Add(new AdapterRelease(adapterId, request.Version, AdapterRelease.PromotedAction, null, requestContext.GetNameIdentifier()));
        await dbContext.SaveChangesAsync();
        return new { AdapterId = adapterId, Current = request.Version };
    }
}
