#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bitween.NativeAdapters.Mapper;
using SW.PrimitiveTypes;

namespace SW.Bitween;

/// <summary>
/// Builds the partner and global values a <c>NativeMapper</c> maps against.
/// </summary>
/// <remarks>
/// Shared by the exchange pipeline and the preview endpoint on purpose. The old mapper's preview
/// assembled this itself and drifted: it injected partner values into a root-array payload where the
/// pipeline did not, so a mapping previewed with a partner value and then failed in production. One
/// factory means that cannot happen twice.
/// </remarks>
public class MappingContextFactory(BitweenDbContext dbContext, IInfolinkCache cache)
{
    public async Task<MappingContext> Build(int? partnerId, string? xchangeId = null,
        IReadOnlyDictionary<string, string>? sourceValues = null)
    {
        var partner = partnerId.HasValue
            ? await dbContext.FindAsync<Partner>(partnerId.Value)
            : null;

        var globals = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        foreach (var set in await cache.ListGlobalAdapterValuesSetsAsync())
            if (set.Values?.Count > 0)
                globals[set.Id] = set.Values;

        return new MappingContext
        {
            Partner = partner?.AdapterProperties ?? new Dictionary<string, string>(),
            Globals = globals,
            Source = sourceValues ?? new Dictionary<string, string>(),
            XchangeId = xchangeId,
        };
    }
}
