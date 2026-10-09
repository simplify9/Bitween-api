using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Adapters;

public class AdapterVersionsRequest
{
    public string AdapterId { get; set; }
}

/// <summary>Every published version of one adapter, with which is current: bitween adapter versions.</summary>
[HandlerName("versions")]
public class Versions(BitweenDbContext dbContext, RequestContext requestContext, AdapterWorkshop workshop)
    : IQueryHandler<AdapterVersionsRequest, object>
{
    public async Task<object> Handle(AdapterVersionsRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);
        if (string.IsNullOrWhiteSpace(request.AdapterId)) throw new SWValidationException("AdapterId", "Say which adapter.");
        return await workshop.VersionsAsync(request.AdapterId.ToLowerInvariant());
    }
}

/// <summary>Takes a published version out of use: bitween adapter withdraw. In the audit trail.</summary>
[HandlerName("withdraw")]
public class Withdraw(BitweenDbContext dbContext, RequestContext requestContext, AdapterWorkshop workshop)
    : ICommandHandler<AdapterPromoteRequest, object>
{
    public async Task<object> Handle(AdapterPromoteRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.AdapterSource.Operate);
        var adapterId = request.AdapterId?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(adapterId) || NativeAdapterDiscoveryService.IsNative(adapterId))
            throw new SWValidationException("AdapterId", "Only a published adapter has versions to withdraw.");

        await workshop.WithdrawAsync(adapterId, request.Version);
        dbContext.Add(new AdapterRelease(adapterId, request.Version, AdapterRelease.WithdrawnAction, null, requestContext.GetNameIdentifier()));
        await dbContext.SaveChangesAsync();
        return new { AdapterId = adapterId, Withdrawn = request.Version };
    }
}
