using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.NativeAdapters.JsonMapper;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Mappers;

public class MapperPreviewRequest
{
    public string ScribanTemplate { get; set; } = "{}";
    public string InputJson { get; set; } = "{}";
    public int? PartnerId { get; set; }
}

public class MapperPreviewResponse
{
    public string? OutputJson { get; set; }
    public string? Error { get; set; }
}

public class Preview : ICommandHandler<MapperPreviewRequest, MapperPreviewResponse>
{
    private readonly RequestContext _requestContext;
    private readonly BitweenDbContext _dbContext;

    public Preview(RequestContext requestContext, BitweenDbContext dbContext)
    {
        _requestContext = requestContext;
        _dbContext = dbContext;
    }

    public async Task<MapperPreviewResponse> Handle(MapperPreviewRequest request)
    {
        _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

        var partner = request.PartnerId.HasValue
            ? await _dbContext.FindAsync<Partner>(request.PartnerId.Value)
            : null;

        var globalSets = await _dbContext.Set<GlobalAdapterValuesSet>().ToListAsync();

        try
        {
            var inputJson = request.InputJson;

            JObject? jObj = null;
            if (JToken.Parse(inputJson) is JObject parsedObj)
                jObj = parsedObj;

            if (jObj != null)
            {
                var enriched = false;

                if (partner?.AdapterProperties?.Count > 0)
                {
                    jObj["__partner__"] = JObject.FromObject(partner.AdapterProperties);
                    enriched = true;
                }

                var nonEmptySets = globalSets.Where(s => s.Values?.Count > 0).ToList();
                if (nonEmptySets.Count > 0)
                {
                    var globalsObj = new JObject();
                    foreach (var set in nonEmptySets)
                        globalsObj[set.Id] = JObject.FromObject(set.Values);
                    jObj["__globals__"] = globalsObj;
                    enriched = true;
                }

                if (enriched)
                    inputJson = jObj.ToString(Formatting.None);
            }

            var output = ScribanJsonHelper.Render(request.ScribanTemplate, inputJson);
            return new MapperPreviewResponse { OutputJson = output };
        }
        catch (Exception ex)
        {
            return new MapperPreviewResponse { Error = ex.Message };
        }
    }
}
