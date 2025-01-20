using System.Threading.Tasks;
using SW.PrimitiveTypes;

namespace SW.Bitween;

public class CacheRevokeService : IListen<RevokeCacheMessage>
{
    private readonly IInfolinkCache _BitweenCache;

    public CacheRevokeService(IInfolinkCache BitweenCache)
    {
        _BitweenCache = BitweenCache;
    }

    public Task Process(RevokeCacheMessage message)
    {
        _BitweenCache.Revoke();
        return Task.CompletedTask;
    }
}