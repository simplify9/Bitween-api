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

public class Preview(RequestContext requestContext, BitweenDbContext dbContext)
    : ICommandHandler<MapperPreviewRequest, MapperPreviewResponse>
{
    public async Task<MapperPreviewResponse> Handle(MapperPreviewRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.Edit);

        var partner = request.PartnerId.HasValue
            ? await dbContext.FindAsync<Partner>(request.PartnerId.Value)
            : null;

        var globalSets = await dbContext.Set<GlobalAdapterValuesSet>().ToListAsync();

        try
        {
            var inputJson = request.InputJson;

            var parsed = JToken.Parse(inputJson);
            var jObj = parsed as JObject;
            var jArr = parsed as JArray;

            // Declared secrets are masked: the template is the caller's own, so it can print any
            // value it is given.
            JObject? partnerObj = partner?.AdapterProperties?.Count > 0
                ? JObject.FromObject(AdapterSecretProperties.Mask(partner.AdapterProperties, partner.SecretProperties))
                : null;

            JObject? globalsObj = null;
            var nonEmptySets = globalSets.Where(s => s.Values?.Count > 0).ToList();
            if (nonEmptySets.Count > 0)
            {
                globalsObj = new JObject();
                foreach (var set in nonEmptySets)
                    globalsObj[set.Id] = JObject.FromObject(AdapterSecretProperties.Mask(set.Values, set.SecretProperties));
            }

            if (jObj != null && (partnerObj != null || globalsObj != null))
            {
                if (partnerObj != null) jObj["__partner__"] = partnerObj;
                if (globalsObj != null) jObj["__globals__"] = globalsObj;
                inputJson = jObj.ToString(Formatting.None);
            }
            else if (jArr != null && (partnerObj != null || globalsObj != null))
            {
                foreach (var token in jArr)
                {
                    if (token is JObject elem)
                    {
                        if (partnerObj != null) elem["__partner__"] = partnerObj;
                        if (globalsObj != null) elem["__globals__"] = globalsObj;
                    }
                }
                inputJson = jArr.ToString(Formatting.None);
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
