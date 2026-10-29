using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Adapters;

public class AdapterSourceRequest
{
    public string AdapterId { get; set; }
    public string Version { get; set; }

    /// <summary>The file, relative to the source folder; for <see cref="SourceFile"/> only.</summary>
    public string Path { get; set; }
}

/// <summary>
/// The files a published version's source holds, with their hashes, so a client can show the tree
/// and tell which files two versions differ in without reading any.
/// </summary>
[HandlerName("source")]
public class Source(
    AdapterSourceReader reader,
    BitweenDbContext dbContext,
    RequestContext requestContext) : IQueryHandler<AdapterSourceRequest, object>
{
    public async Task<object> Handle(AdapterSourceRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.AdapterSource.View);

        var listing = await reader.ListAsync(request.AdapterId?.ToLowerInvariant(), request.Version)
                      ?? throw new SWNotFoundException($"{request.AdapterId} {request.Version}");
        return listing;
    }
}

/// <summary>One source file of a published version, checked against its manifest's hash. Each read is audited.</summary>
[HandlerName("sourcefile")]
public class SourceFile(
    AdapterSourceReader reader,
    BitweenDbContext dbContext,
    RequestContext requestContext) : IQueryHandler<AdapterSourceRequest, object>
{
    public async Task<object> Handle(AdapterSourceRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.AdapterSource.View);

        var adapterId = request.AdapterId?.ToLowerInvariant();
        var file = await reader.ReadAsync(adapterId, request.Version, request.Path)
                   ?? throw new SWNotFoundException($"{request.AdapterId} {request.Version} {request.Path}");

        dbContext.Add(new AdapterSourceAccess(adapterId, request.Version, request.Path, requestContext.GetNameIdentifier()));
        await dbContext.SaveChangesAsync();
        return file;
    }
}
