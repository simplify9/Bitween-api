using System;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.NativeAdapters.JsonMapper;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Mappers;

public class MapperPreviewRequest
{
    public string ScribanTemplate { get; set; } = "{}";
    public string InputJson { get; set; } = "{}";
}

public class MapperPreviewResponse
{
    public string? OutputJson { get; set; }
    public string? Error { get; set; }
}

public class Preview : ICommandHandler<MapperPreviewRequest, MapperPreviewResponse>
{
    private readonly RequestContext _requestContext;

    public Preview(RequestContext requestContext)
    {
        _requestContext = requestContext;
    }

    public Task<MapperPreviewResponse> Handle(MapperPreviewRequest request)
    {
        _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

        try
        {
            var output = ScribanJsonHelper.Render(request.ScribanTemplate, request.InputJson);
            return Task.FromResult(new MapperPreviewResponse { OutputJson = output });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new MapperPreviewResponse { Error = ex.Message });
        }
    }
}
