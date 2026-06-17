using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SW.Bitween.Domain;
using SW.Bitween.NativeAdapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters;

public class NativeUpdatePartnerPropsHandler : INativeInfolinkHandler
{
    private readonly BitweenDbContext _dbContext;

    public NativeUpdatePartnerPropsHandler(BitweenDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "NativeUpdatePartnerPropsHandler";

    public void InitializeStartupValues(IDictionary<string, string> settings) { }

    public Type StartupValuesType => typeof(NativeUpdatePartnerPropsHandlerInput);

    public async Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        var payload = JsonConvert.DeserializeObject<PartnerPropsPayload>(xchangeFile.Data)
            ?? throw new Exception("Invalid payload: expected { partnerId, properties }");

        var partner = await _dbContext.FindAsync<Partner>(payload.PartnerId)
            ?? throw new Exception($"Partner {payload.PartnerId} not found.");

        var existing = partner.AdapterProperties ?? new Dictionary<string, string>();
        foreach (var (key, value) in payload.Properties)
            existing[key] = value;

        partner.AdapterProperties = existing;
        _dbContext.Entry(partner).Property(p => p.AdapterProperties).IsModified = true;
        await _dbContext.SaveChangesAsync();

        return null!;
    }

    private class PartnerPropsPayload
    {
        public int PartnerId { get; set; }
        public Dictionary<string, string> Properties { get; set; } = new();
    }
}

public class NativeUpdatePartnerPropsHandlerInput { }
