using System.Threading.Tasks;
using SW.PrimitiveTypes;

namespace SW.Infolink;

public class CacheRevokeService : IListen<RevokeCacheMessage>
{
    private readonly IInfolinkCache _infolinkCache;

    public CacheRevokeService(IInfolinkCache infolinkCache)
    {
        _infolinkCache = infolinkCache;
    }

    public Task Process(RevokeCacheMessage message)
    {
        _infolinkCache.Revoke();
        return Task.CompletedTask;
    }
}