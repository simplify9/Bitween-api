using System.Threading.Tasks;
using SW.Bitween.Services;
using SW.PrimitiveTypes;

namespace SW.Bitween;

public class CacheRevokeService : IListen<RevokeCacheMessage>
{
    private readonly IInfolinkCache _BitweenCache;
    private readonly SettingsService _settings;
    private readonly BitweenDbContext _dbContext;

    public CacheRevokeService(IInfolinkCache BitweenCache, SettingsService settings, BitweenDbContext dbContext)
    {
        _BitweenCache = BitweenCache;
        _settings = settings;
        _dbContext = dbContext;
    }

    public async Task Process(RevokeCacheMessage message)
    {
        _BitweenCache.Revoke();
        // Settings live on singletons rather than in the cache, so they need their own refresh:
        // this is how an instance picks up a setting changed on a different instance.
        await _settings.Reload(_dbContext);
    }
}
