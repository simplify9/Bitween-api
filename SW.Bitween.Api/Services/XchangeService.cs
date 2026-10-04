using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.Bitween.Services.Adapters;
using SW.Bus.RabbitMqExtensions;

namespace SW.Bitween;

public class XchangeService(BitweenOptions BitweenSettings, BitweenDbContext dbContext,
    FilterService filterService,
    ICloudFilesService cloudFiles, IServiceProvider serviceProvider,
    IPublish publish, ILogger<XchangeService> logger, IInfolinkCache BitweenCache,
    IAdapterInvoker adapterInvoker, NativeAdapterDiscoveryService nativeAdapterDiscovery,
    FileLinks fileLinks, StorageRetention storageRetention) :
    // IConsume<ApiXchangeCreatedEvent>,
    // IConsume<InternalXchangeCreatedEvent>,
    // IConsume<AggregateXchangeCreatedEvent>,
    // IConsume<ReceivingXchangeCreatedEvent>,
    // IConsume<XchangeResultCreatedEvent>,
    IConsume<SubscriptionUnpausedEvent>,
    IConsumeExtended

{
    public const string ResultQueueSuffix = "-Result";

    public async Task<string> SubmitSubscriptionXchange(int subscriptionId, XchangeFile file,
        string[] references = null, Partner gatewayPartner = null,
        GlobalAdapterValuesSet[] globalAdapterValuesSets = null)
    {
        var subscription = await BitweenCache.SubscriptionByIdAsync(subscriptionId);

        var xchange = await CreateXchange(subscription, file, references, Guid.NewGuid().ToString("N"), gatewayPartner,
            globalAdapterValuesSets);
        await dbContext.SaveChangesAsync();
        return xchange.Id;
    }

    public async Task SubmitFilterXchange(int documentId, XchangeFile file, string[] references = null,
        string correlationId = null)
    {
        var document = await BitweenCache.DocumentByIdAsync(documentId);
        Xchange xchange;

        if (document?.DisregardsUnfilteredMessages ?? false)
        {
            xchange = new Xchange(documentId, null, file, references, SubscriptionType.Internal, correlationId);
            var result = await filterService.Filter(xchange.DocumentId, file);
            await CreateXchangesForHits(xchange, result, file);
        }
        else
        {
            xchange = await CreateXchange(document, null, file, references, correlationId);
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task CreateXchange(Xchange xchange, XchangeFile file, WorkGroup workGroup,
        bool manualRetry = false)
    {
        await EnsureNotAlreadyRetried(xchange.Id);
        if (xchange.DocumentId == Document.AggregationDocumentId) file = WithServedLinks(file);
        var newXchange = new Xchange(xchange, file, workGroup, manualRetry);
        await AddInputFile(newXchange, file);
        dbContext.Add(newXchange);
    }

    public async Task CreateXchange(Subscription subscription, Xchange xchange, XchangeFile file,
        string[] references = null, Dictionary<string, int> groupAttemptCounts = null, bool manualRetry = false)
    {
        await EnsureNotAlreadyRetried(xchange.Id);
        if (xchange.DocumentId == Document.AggregationDocumentId) file = WithServedLinks(file);
        var partnerId = xchange.PartnerId ?? subscription.PartnerId;
        var partner = partnerId.HasValue ? await dbContext.FindAsync<Partner>(partnerId.Value) : null;
        var globalAdapterValuesSets = await BitweenCache.ListGlobalAdapterValuesSetsAsync();
        var newXchange = new Xchange(subscription, xchange, file, partner, globalAdapterValuesSets,
            groupAttemptCounts, manualRetry);
        await AddInputFile(newXchange, file);
        dbContext.Add(newXchange);
    }

    private static readonly Regex ExchangeFileKey = new("/[0-9a-f]{32}/(input|output|response)$");

    /// <summary>
    /// A roll-up made before Bitween served its own file links lists storage URLs, which stop opening once
    /// the bucket is private. Retrying one — with its old properties or reset ones — swaps each URL of an
    /// exchange file in Bitween's bucket for a link Bitween serves, so the handler reading it still gets
    /// every file. Anything else is left as it was.
    /// </summary>
    private XchangeFile WithServedLinks(XchangeFile rollUp)
    {
        List<string> urls;
        try
        {
            urls = JsonConvert.DeserializeObject<List<string>>(rollUp.Data);
        }
        catch (JsonException)
        {
            return rollUp;
        }

        // What the storage puts in front of a key differs by provider, so it's read off a URL of its own.
        const string marker = "bitween-key";
        var markerUrl = cloudFiles.GetUrl(marker);
        if (urls == null || !markerUrl.EndsWith(marker, StringComparison.Ordinal)) return rollUp;
        var storageBase = markerUrl[..^marker.Length];

        var swapped = false;
        for (var i = 0; i < urls.Count; i++)
        {
            if (urls[i]?.StartsWith(storageBase, StringComparison.Ordinal) != true) continue;
            var key = Uri.UnescapeDataString(urls[i][storageBase.Length..]);
            if (!ExchangeFileKey.IsMatch(key) || fileLinks.LinkTo(key, forAdapter: true) is not { } link) continue;
            urls[i] = link;
            swapped = true;
        }

        return swapped ? new XchangeFile(JsonConvert.SerializeObject(urls), rollUp.Filename, rollUp.BadData) : rollUp;
    }

    public async Task<Xchange> CreateXchange(Document document, WorkGroup workGroup, XchangeFile file,
        string[] references = null,
        string correlationId = null)
    {
        var xchange = new Xchange(document.Id, workGroup, file, references, SubscriptionType.Internal, correlationId);
        await AddInputFile(xchange, file);
        dbContext.Add(xchange);
        return xchange;
    }

    public async Task<Xchange> CreateXchange(Subscription subscription, XchangeFile file,
        string[] references = null, string correlationId = null, Partner gatewayPartner = null,
        GlobalAdapterValuesSet[] globalAdapterValuesSets = null)
    {
        // Callers that have no partner/globals context of their own (scheduled receivers,
        // aggregation, manual "create exchange", plain internal subscription fan-out) leave
        // this null — resolve it here so {{globals.…}} always gets a chance to translate,
        // instead of silently no-op'ing for whichever caller forgot to load it.
        globalAdapterValuesSets ??= await BitweenCache.ListGlobalAdapterValuesSetsAsync();

        // And the same for the partner, for the same reason. Only a caller that learned the
        // partner from somewhere other than the subscription — a bus gateway route, a partner
        // calling an API gateway — has one to hand in; everyone else left it null and the
        // subscription's own partner went unused, so every {{partner.…}} in its adapters was
        // written out literally and the handler ran against the template. This is the value
        // the Xchange is attributed to either way (see PartnerId below), so filling from it
        // adds a resolution that was missing rather than changing whose exchange it is.
        gatewayPartner ??= subscription.PartnerId.HasValue
            ? await dbContext.FindAsync<Partner>(subscription.PartnerId.Value)
            : null;

        var xchange = new Xchange(subscription, file, references, correlationId, gatewayPartner,
            globalAdapterValuesSets);
        await AddInputFile(xchange, file);
        dbContext.Add(xchange);
        return xchange;
    }

    /// <summary>
    /// Executes a due or manually-triggered <see cref="DelayedRetry"/>: resubmits the original
    /// failed Xchange and removes the DelayedRetry record. Used by both <c>RetryJob</c> (scheduled)
    /// and the <c>DelayedRetries/RunNow</c> endpoint (immediate).
    /// </summary>
    /// <returns><c>false</c> if the original Xchange or its Subscription no longer exist (the
    /// DelayedRetry record is removed as an orphan in that case); <c>true</c> on success.</returns>
    public async Task<bool> ExecuteDelayedRetry(DelayedRetry delayedRetry)
    {
        var xchange = await dbContext.FindAsync<Xchange>(delayedRetry.Id);
        if (xchange == null)
        {
            dbContext.Remove(delayedRetry);
            return false;
        }

        var subscription = await dbContext.Subscriptions()
            .FirstOrDefaultAsync(s => s.Id == xchange.SubscriptionId);
        if (subscription == null)
        {
            // Recorded on the result like the unreadable-input case below, rather than only dropping
            // the schedule: the exchange is still there for someone to look at, so leaving it with no
            // reason means the retry simply stopped happening with nothing to explain it.
            dbContext.Remove(delayedRetry);

            var orphaned = await dbContext.FindAsync<XchangeResult>(xchange.Id);
            orphaned?.SetRetryBlocked(
                "The scheduled retry was dropped: the subscription it belonged to no longer exists.");
            return false;
        }

        var alreadyRetried = await FindRetryOf(xchange.Id);
        if (alreadyRetried != null)
        {
            // Reached only if a manual retry got in first — the endpoint refuses that while a
            // retry is scheduled, so it takes a race to arrive here. Dropped like the cases below
            // rather than left to throw: an exception here would leave the schedule in place and
            // the job would pick the same impossible retry up again on every pass, forever.
            dbContext.Remove(delayedRetry);

            var retried = await dbContext.FindAsync<XchangeResult>(xchange.Id);
            retried?.SetRetryBlocked(
                $"The scheduled retry was dropped: this exchange had already been retried, as {alreadyRetried}.");
            return false;
        }

        var inputFile = await ReadInputFile(xchange);
        if (inputFile == null)
        {
            // The input is what a retry re-sends, so without it there is nothing to retry with. Handled
            // like a missing subscription — drop the schedule and move on — but recorded on the result
            // as well, because unlike a deleted subscription this needs someone to look into it.
            dbContext.Remove(delayedRetry);

            var result = await dbContext.FindAsync<XchangeResult>(xchange.Id);
            result?.SetRetryBlocked("The scheduled retry was dropped: the input file could not be read.");
            return false;
        }

        await CreateXchange(subscription, xchange, inputFile);
        dbContext.Remove(delayedRetry);
        return true;
    }

    /// <summary>
    /// The original input, or <c>null</c> when it cannot be read — deleted from storage, expired by a
    /// lifecycle rule, or storage itself unavailable.
    /// </summary>
    public async Task<XchangeFile> ReadInputFile(Xchange xchange)
    {
        try
        {
            return new XchangeFile(await GetFile(xchange, XchangeFileType.Input), xchange.InputName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The input file of xchange {XchangeId} could not be read.", xchange.Id);
            return null;
        }
    }

    private Task CreateOnHoldXchange(Subscription subscription, XchangeFile file, string[] references = null,
        int? partnerId = null, string correlationId = null)
    {
        var xchange = new OnHoldXchange(subscription, file.Data, file.Filename, file.BadData, references,
            partnerId, correlationId);
        dbContext.Add(xchange);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts the subscription a delivery's response is handed to, or holds it while that one is
    /// paused. Returns the exchange it started, if any.
    /// </summary>
    /// <remarks>
    /// The delivery has already happened by now, so a target that simply is not there must not
    /// throw: the exception would mark this exchange failed, and its retry would deliver a second
    /// time. A disabled subscription is not in the cache, and is skipped the way a disabled bus
    /// route's subscription is.
    /// </remarks>
    private async Task<Xchange> HandOnResponse(Xchange xchange, XchangeFile responseFile)
    {
        var target = await BitweenCache.SubscriptionByIdAsync(xchange.ResponseSubscriptionId!.Value);
        if (target == null)
        {
            logger.LogWarning(
                "The response of xchange {XchangeId} goes to subscription {SubscriptionId}, which is not active; skipping.",
                xchange.Id, xchange.ResponseSubscriptionId);
            return null;
        }

        // Legacy targets (Internal, ApiCall) keep what they have always had: every response, run
        // as their own partner. A response subscription is shared by everything that feeds it, so
        // it runs as the partner of the one that did, and only on a bad response if it says so.
        var isResponseType = target.Type == SubscriptionType.Response;
        if (isResponseType && responseFile.BadData && !target.RunOnBadResponses)
            return null;

        var partnerId = isResponseType ? xchange.PartnerId : null;

        if (target.PausedOn != null)
        {
            await CreateOnHoldXchange(target, responseFile, null, partnerId, xchange.CorrelationId);
            return null;
        }

        var partner = partnerId.HasValue ? await dbContext.FindAsync<Partner>(partnerId.Value) : null;
        return await CreateXchange(target, responseFile, null, xchange.CorrelationId, partner);
    }


    /// <summary>
    /// Collects the partner and global values for a mapper that takes them as context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same values the enrichment below writes into the payload, gathered into their own object
    /// instead. That is what lets a document which is not JSON be mapped, and it means a document
    /// carrying a real <c>__partner__</c> field keeps it.
    /// </para>
    /// <para>
    /// Global values come from the cache, not a fresh query. The enrichment path reads them with
    /// <c>dbContext.Set&lt;GlobalAdapterValuesSet&gt;()</c> on every exchange while the rest of this
    /// service goes through <c>BitweenCache</c>; that is left alone rather than corrected, because
    /// changing when existing subscriptions see an edited value is not this change's business.
    /// </para>
    /// </remarks>
    private async Task<string> BuildMappingContextJson(Xchange xchange)
    {
        var factory = serviceProvider.GetRequiredService<MappingContextFactory>();
        return JsonConvert.SerializeObject(await factory.Build(xchange.PartnerId, xchange.Id));
    }

    private async Task<XchangeFile> RunMapper(Xchange xchange, XchangeFile xchangeFile)
    {
        if (xchange.MapperId == null) return xchangeFile;

        // A mapper that takes its context separately gets the payload exactly as it arrived. Anything
        // else keeps the enrichment below, unchanged — every template already written reads
        // __partner__ out of the payload, so this is load-bearing behaviour, not an implementation
        // detail. Note the JToken.Parse: it runs before anything knows which mapper is configured,
        // and it throws on a payload that is not JSON, which is why a mapper that wants to handle
        // XML or CSV has to be able to opt out of this entire block.
        string mappingContextJson = null;

        if (nativeAdapterDiscovery.MapperReceivesOwnContext(xchange.MapperId))
        {
            mappingContextJson = await BuildMappingContextJson(xchange);
        }
        else
        {
            // Inject __partner__ adapter properties into the input JSON so Scriban templates
            // can reference them as {{ __partner__?.propkey }}
            // Only applies when the data is a JSON object; skip enrichment for non-object payloads
            // (e.g. a receiver returning a JSON-encoded string).
            var jObjEnriched = JToken.Parse(xchangeFile.Data) as JObject;
            var enriched = false;

            if (jObjEnriched != null)
            {
                if (xchange.PartnerId.HasValue)
                {
                    var partner = await dbContext.FindAsync<Partner>(xchange.PartnerId.Value);
                    if (partner?.AdapterProperties?.Count > 0)
                    {
                        jObjEnriched["__partner__"] = JObject.FromObject(partner.AdapterProperties);
                        enriched = true;
                    }
                }

                // Inject __globals__ — all global adapter values sets
                // so templates can use {{ __globals__?.setId?.key }}
                var globalSets = await dbContext.Set<GlobalAdapterValuesSet>().ToListAsync();
                if (globalSets.Any(s => s.Values?.Count > 0))
                {
                    var globalsObj = new JObject();
                    foreach (var set in globalSets.Where(s => s.Values?.Count > 0))
                        globalsObj[set.Id] = JObject.FromObject(set.Values);
                    jObjEnriched["__globals__"] = globalsObj;
                    enriched = true;
                }

                if (enriched)
                    xchangeFile = new XchangeFile(jObjEnriched.ToString(Formatting.None), xchangeFile.Filename);
            }
        }

        var mapperProperties = xchange.MapperProperties.ToDictionary();
        mapperProperties["xchangeid"] = xchange.Id;

        if (mappingContextJson != null)
            mapperProperties[NativeAdapters.Mapper.NativeMapper.ContextKey] = mappingContextJson;

        // Check if it's a native adapter
        // No branching on the adapter's kind: the invoker decides which of the three runtimes
        // owns this id — in-process, spawned, or a rented resident instance — and the pipeline
        // only says what it wants run.
        xchangeFile = await adapterInvoker.InvokeAsync<XchangeFile>(
            xchange.MapperId, AdapterRole.Mapper, nameof(IInfolinkHandler.Handle), xchangeFile,
            mapperProperties, xchange.CorrelationId ?? xchange.Id);

        if (xchangeFile is null)
            throw new BitweenException(
                $"Unexpected null return value after running mapping for exchange id: {xchange.Id}, adapter id: {xchange.MapperId}");
        else
            await AddFile(xchange, XchangeFileType.Output, xchangeFile);
        return xchangeFile;
    }

    public async Task RunValidator(string validatorId, IDictionary<string, string> properties,
        XchangeFile xchangeFile)
    {
        if (validatorId == null) return;

        var result = await adapterInvoker.InvokeAsync<InfolinkValidatorResult>(
            validatorId, AdapterRole.Validator, nameof(IInfolinkValidator.Validate),
            xchangeFile, properties);

        if (!result.Success)
            throw new SWValidationException(result.Validations);
    }

    private async Task<XchangeFile> RunHandler(Xchange xchange, XchangeFile xchangeFile)
    {
        if (xchange.HandlerId == null) return null;

        var handlerProperties = xchange.HandlerProperties.ToDictionary();
        handlerProperties["xchangeid"] = xchange.Id;

        xchangeFile = await adapterInvoker.InvokeAsync<XchangeFile>(
            xchange.HandlerId, AdapterRole.Handler, nameof(IInfolinkHandler.Handle), xchangeFile,
            handlerProperties, xchange.CorrelationId ?? xchange.Id);

        if (xchangeFile != null)
            await AddFile(xchange, XchangeFileType.Response, xchangeFile);
        return xchangeFile;
    }

    // private T InstantiateNativeAdapter<T>(string adapterId, IDictionary<string, string> properties)
    // {
    //     var adapterInfo = nativeAdapterDiscovery.GetNativeAdapterInfo(adapterId);
    //     if (adapterInfo == null)
    //         throw new BitweenException($"Native adapter not found: {adapterId}");
    //
    //     // Get the constructor that takes a parameter
    //     var constructor = adapterInfo.Type.GetConstructors()
    //         .FirstOrDefault(c => c.GetParameters().Length > 0);
    //
    //     if (constructor == null)
    //         throw new BitweenException(
    //             $"Native adapter {adapterId} must have a constructor that accepts an input model");
    //
    //     // Get the input parameter type
    //     var inputParameter = constructor.GetParameters().First();
    //     var inputType = inputParameter.ParameterType;
    //
    //     // Create an instance of the input model by mapping properties
    //     var inputInstance = Activator.CreateInstance(inputType);
    //
    //     // Map dictionary properties to the input model
    //     foreach (var prop in inputType.GetProperties())
    //     {
    //         // Case-insensitive property lookup
    //         var propEntry = properties.FirstOrDefault(p =>
    //             string.Equals(p.Key, prop.Name, StringComparison.OrdinalIgnoreCase));
    //
    //         if (!string.IsNullOrEmpty(propEntry.Key))
    //         {
    //             var value = propEntry.Value;
    //             try
    //             {
    //                 var convertedValue = Convert.ChangeType(value,
    //                     Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType);
    //                 prop.SetValue(inputInstance, convertedValue);
    //             }
    //             catch
    //             {
    //                 // If conversion fails, set string value directly
    //                 if (prop.PropertyType == typeof(string))
    //                     prop.SetValue(inputInstance, value);
    //             }
    //         }
    //     }
    //
    //     // Instantiate the adapter with the input model
    //     var adapter = Activator.CreateInstance(adapterInfo.Type, inputInstance);
    //
    //     return (T)adapter;
    // }

    /// <summary>
    /// Writes a new exchange's input under the document prefix in force now, and records that prefix on
    /// the exchange so its later files go next to it and a change of prefix never loses track of them.
    /// </summary>
    private Task AddInputFile(Xchange xchange, XchangeFile file)
    {
        xchange.StoreFilesUnder(BitweenSettings.DocumentPrefix);
        return AddFile(xchange, XchangeFileType.Input, file);
    }

    private async Task AddFile(Xchange xchange, XchangeFileType type, XchangeFile file)
    {
        var key = FileKey(xchange, type);
        try
        {
            // Always private. Readers without a Bitween login get a sealed link from FileLinks instead
            // of a storage URL, which only ever opened because the file was public.
            await cloudFiles.WriteTextAsync(file.Data, new WriteFileSettings { Key = key });
            logger.LogDebug("Wrote the {FileType} file of xchange {XchangeId} to {Key}.", type, xchange.Id, key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not write the {FileType} file of xchange {XchangeId} to {Key}.",
                type, xchange.Id, key);
            throw;
        }
    }

    /// <summary>
    /// The prefix an exchange's files were written under: the one it recorded, or for an exchange from
    /// before exchanges recorded one, the prefix in force back then.
    /// </summary>
    public string FilesPrefixOf(string recordedPrefix) =>
        recordedPrefix ?? BitweenSettings.LegacyDocumentPrefix ?? BitweenSettings.DocumentPrefix;

    public string FileKey(Xchange xchange, XchangeFileType type) => FileKey(xchange.Id, xchange.FilesPrefix, type);

    public string FileKey(string xchangeId, string recordedPrefix, XchangeFileType type) =>
        $"{FilesPrefixOf(recordedPrefix)}/{xchangeId}/{type.ToString().ToLower()}";

    /// <summary>The key of a file the exchange has, or <c>null</c> when it has none: an empty size means it was never written.</summary>
    public string FileKey(string xchangeId, string recordedPrefix, int? fileSize, XchangeFileType type) =>
        fileSize is null or 0 ? null : FileKey(xchangeId, recordedPrefix, type);

    /// <summary>
    /// A link to an exchange file for a reader without a login (see <see cref="FileLinks"/>). Falls back to
    /// the storage URL when there's no address to build a link on, which only opens a file written while
    /// exchange files were still public.
    /// </summary>
    public string FileUrl(string xchangeId, string recordedPrefix, XchangeFileType type)
    {
        var key = FileKey(xchangeId, recordedPrefix, type);
        return fileLinks.LinkTo(key) ?? cloudFiles.GetUrl(key);
    }

    /// <summary><see cref="FileUrl(string,string,XchangeFileType)"/> for a file the exchange has, <c>null</c> for one it doesn't.</summary>
    public string FileUrl(string xchangeId, string recordedPrefix, int? fileSize, XchangeFileType type) =>
        fileSize is null or 0 ? null : FileUrl(xchangeId, recordedPrefix, type);

    /// <summary>
    /// A link to an exchange file for an adapter Bitween runs, such as an aggregation's handler: built on
    /// <see cref="FileLinks.AdapterBaseUrl"/>, never on the address of whoever made a request. Throws rather
    /// than fall back to the storage URL, which doesn't open now that exchange files are private.
    /// </summary>
    public string AdapterFileUrl(string xchangeId, string recordedPrefix, XchangeFileType type) =>
        fileLinks.LinkTo(FileKey(xchangeId, recordedPrefix, type), forAdapter: true)
        ?? throw new BitweenException("Bitween has no address to build links to exchange files on.");

    /// <summary>
    /// The exchange a storage key belongs to, and which of its files it is — <c>null</c> when the key
    /// isn't exactly where one of an exchange's files lives. How a request naming a raw key is held to
    /// exchange files and nothing else in the bucket.
    /// </summary>
    public async Task<(Xchange Xchange, XchangeFileType Type)?> FindFileOwner(string key)
    {
        var parts = key?.Split('/');
        if (parts is not { Length: >= 3 } || !Enum.TryParse<XchangeFileType>(parts[^1], true, out var type))
            return null;

        var id = parts[^2];
        var xchange = await dbContext.Set<Xchange>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return xchange != null && FileKey(xchange, type) == key ? (xchange, type) : null;
    }

    public async Task<string> GetFile(string xchangeId, XchangeFileType type) =>
        await GetFile(await dbContext.FindAsync<Xchange>(xchangeId)
                      ?? throw new BitweenException($"Xchange '{xchangeId}' not found."), type);

    /// <summary>Reads one of an exchange's files whole; see <see cref="OpenFile"/>.</summary>
    public async Task<string> GetFile(Xchange xchange, XchangeFileType type)
    {
        await using var cloudStream = await OpenFile(xchange, type);
        using var reader = new StreamReader(cloudStream);
        return await reader.ReadToEndAsync();
    }

    /// <summary>
    /// Opens one of an exchange's files to read. A file that isn't there any more is reported as such —
    /// deleted by the bucket's retention rule when one covers it and the exchange is old enough, or else
    /// missing — rather than as the storage provider's own error.
    /// </summary>
    public async Task<Stream> OpenFile(Xchange xchange, XchangeFileType type)
    {
        var key = FileKey(xchange, type);
        try
        {
            return await cloudFiles.OpenReadAsync(key);
        }
        catch (Exception ex) when (StorageErrors.IsNotFound(ex))
        {
            var rules = await storageRetention.GetAsync();
            var rule = rules.RuleFor(key);
            if (rule != null && (DateTime.UtcNow - xchange.StartedOn).TotalDays >= rule.Days)
            {
                logger.LogInformation("The {FileType} file of xchange {XchangeId} was deleted by the retention rule for {Prefix}.",
                    type, xchange.Id, rule.Prefix);
                throw new SWValidationException("FILE_EXPIRED",
                    $"This file was deleted by the storage retention policy: files under {rule.Prefix} are kept " +
                    $"{rule.Days} days, and this exchange started on {xchange.StartedOn:yyyy-MM-dd}.");
            }

            logger.LogError(ex, "The {FileType} file of xchange {XchangeId} is missing from {Key}.", type, xchange.Id, key);
            throw new SWValidationException("FILE_MISSING", rules.Problem == null
                ? "This file isn't in storage. No retention rule explains it, so it was removed some other way."
                : "This file isn't in storage. The bucket's deletion rules couldn't be read, so whether one removed it can't be told.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not read the {FileType} file of xchange {XchangeId} from {Key}.",
                type, xchange.Id, key);
            throw;
        }
    }

    private async Task Process(XchangeMessage message)
    {
        Xchange responseXchange = null;
        XchangeFile outputFile = null;
        XchangeFile responseFile = null;
        WorkGroup workGroup = null;
        var xchange = await dbContext.FindAsync<Xchange>(message.Id);

        if (xchange == null) throw new BitweenException($"Xchange '{message.Id}' not found.");

        try
        {
            var inputFile = new XchangeFile(await GetFile(xchange, XchangeFileType.Input), xchange.InputName);
            var result = await filterService.Filter(xchange.DocumentId, inputFile);

            dbContext.Add(new XchangePromotedProperties(xchange.Id, result));

            if (xchange.SubscriptionId != null)
            {
                workGroup = await BitweenCache.WorkGroupBySubscriptionIdAsync(xchange.SubscriptionId.Value);
                if (xchange.MapperId == null)
                    responseFile = await RunHandler(xchange, inputFile);
                else
                {
                    outputFile = await RunMapper(xchange, inputFile);
                    responseFile = await RunHandler(xchange, outputFile);
                }

                if (xchange.ResponseSubscriptionId != null && responseFile != null)
                    responseXchange = await HandOnResponse(xchange, responseFile);

                if (!string.IsNullOrWhiteSpace(xchange.ResponseMessageTypeName) && responseFile != null &&
                    !responseFile.BadData)
                {
                    await publish.Publish(xchange.ResponseMessageTypeName, responseFile.Data);
                }
            }
            else if (xchange.SubscriptionId == null)
            {
                await CreateXchangesForHits(xchange, result, inputFile);
            }

            var xchangeResult = new XchangeResult(xchange.Id, workGroup, outputFile, responseFile,
                responseXchange?.Id);
            dbContext.Add(xchangeResult);
            if (responseFile?.BadData == true)
                await TrySchedulingWithoutLosingTheResult(xchange, XchangeResultType.BadResult, responseFile.Data,
                    xchangeResult);
            else
                await TryClearingRetryBudgetAfterSuccess(xchange);
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            var xchangeResult = new XchangeResult(xchange.Id, workGroup, outputFile, responseFile,
                responseXchange?.Id, ex.ToString());
            dbContext.Add(xchangeResult);
            await TrySchedulingWithoutLosingTheResult(xchange, XchangeResultType.Error, ex.ToString(), xchangeResult);
            await dbContext.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Evaluates the retry policy without ever costing the caller its failure record.
    /// </summary>
    /// <remarks>
    /// Scheduling runs before the <see cref="XchangeResult"/> is saved and touches the database
    /// several times. Letting it throw would replace the original exception with its own and abort
    /// the save, so the failure would vanish from the UI entirely and only reappear as a silent
    /// redelivery. Losing the retry is recoverable; losing the record of what went wrong is not.
    /// </remarks>
    private async Task TrySchedulingWithoutLosingTheResult(Xchange xchange, XchangeResultType resultType,
        string content, XchangeResult xchangeResult)
    {
        try
        {
            await TryScheduleAutoRetry(xchange, resultType, content, xchangeResult);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Auto-retry evaluation failed for xchange {XchangeId}; the failure result is still recorded.",
                xchange.Id);
        }
    }

    /// <summary>
    /// Gives the subscription its retry budget back after a success, without ever costing the caller
    /// its successful result.
    /// </summary>
    /// <remarks>
    /// Guarded for the same reason scheduling is, and with more at stake: this runs after the handler
    /// has already delivered, so letting it throw would abort the save of a result whose side effects
    /// have happened, and the redelivery would repeat them. A budget left spent is a nuisance somebody
    /// can undo by hand; a duplicated delivery cannot be undone at all.
    /// </remarks>
    private async Task TryClearingRetryBudgetAfterSuccess(Xchange xchange)
    {
        if (xchange.SubscriptionId == null) return;

        try
        {
            // The exchange's own start time is the watermark: anything charged after this run began
            // belongs to a failure this success knows nothing about, and is left where it is.
            await new RetryGroupBudget(dbContext, serviceProvider, xchange.SubscriptionId.Value)
                .ReleaseExhaustedBudgets(xchange.StartedOn);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Retry budget of subscription {SubscriptionId} could not be cleared after a success; " +
                "it may still refuse retries until it is reset.", xchange.SubscriptionId.Value);
        }
    }

    private async Task TryScheduleAutoRetry(Xchange xchange, XchangeResultType resultType, string content,
        XchangeResult xchangeResult)
    {
        if (xchange.SubscriptionId == null) return;

        // A person asked for this attempt, so the policy stays out of it. Otherwise pressing Retry
        // spends a slot of the group's shared total — the budget meant for unattended retries — and
        // can be what finally exhausts it and raises the alert. Recorded rather than skipped
        // silently, so the absence of a follow-up attempt has a visible reason.
        if (xchange.ManualRetry)
        {
            xchangeResult.SetRetryBlocked(
                "This attempt was started by hand, so the retry policy left it alone and its budget is untouched.");
            return;
        }

        // DelayedRetry.Id is xchange.Id, so an existing row means this failure was already
        // evaluated and already spent a slot of the group's total budget. Re-evaluating it
        // (e.g. on an at-least-once redelivery) would both violate the PK on Add and spend a
        // second slot for the same failure.
        var alreadyScheduled = await dbContext.Set<DelayedRetry>().FindAsync(xchange.Id);
        if (alreadyScheduled != null) return;

        var subscription = await dbContext.Set<Subscription>()
            .Include(s => s.RetryPolicy)
            .FirstOrDefaultAsync(s => s.Id == xchange.SubscriptionId.Value);

        IRetryPolicy policy = subscription?.CustomRetryPolicy ?? (IRetryPolicy)subscription?.RetryPolicy;
        if (policy?.Groups == null || policy.Groups.Count == 0) return;

        var evaluator = new RetryPolicyEvaluator(policy,
            new RetryGroupBudget(dbContext, serviceProvider, xchange.SubscriptionId.Value));

        var attemptIndex = await CountRetryChainDepth(xchange);
        var decision = await evaluator.Evaluate(resultType, content, attemptIndex);

        // Which group owned this failure, so the group's retries can later be listed without
        // re-deriving the match, and how deep the chain already was without walking it again.
        if (decision.MatchedGroup is not null)
            xchangeResult.SetRetryEvaluation(decision.MatchedGroup.Id, attemptIndex);

        if (decision.ShouldRetry)
            dbContext.Add(new DelayedRetry
            {
                Id = xchange.Id,
                On = DateTime.UtcNow + decision.Delay
            });
        else
            // A policy applied but refused. Recorded so an exhausted budget is distinguishable
            // from an error no group was ever configured to catch.
            xchangeResult.SetRetryBlocked(decision.Reason);

        // Raised on the result rather than published here, so the alert only reaches the bus once
        // this failure is committed. Its own event type means its own queue and its own consumer,
        // keeping a slow alert handler away from the ordinary notifier path.
        if (decision.BudgetJustExhausted)
            xchangeResult.RaiseBudgetExhausted(
                xchange.SubscriptionId.Value,
                decision.MatchedGroup!.Id,
                decision.MatchedGroup.Name,
                decision.MatchedGroup.Budget!.MaxAttemptsTotal!.Value);
    }

    /// <summary>
    /// The retry, if any, already made from <paramref name="xchangeId"/>.
    /// </summary>
    private Task<string> FindRetryOf(string xchangeId) =>
        dbContext.Set<Xchange>().AsNoTracking()
            .Where(x => x.RetryFor == xchangeId)
            .Select(x => x.Id)
            .FirstOrDefaultAsync();

    /// <summary>
    /// An exchange gets at most one retry, so that the attempts made from one original form a
    /// single chain that can be read end to end. Retrying an exchange that already has one would
    /// fork it: two attempts from the same starting point, neither of them the current state of
    /// anything, and no way to say which one "the retry" of the original was.
    /// </summary>
    /// <remarks>
    /// Enforced here rather than at each endpoint so that every way of asking for a retry — by
    /// hand, in bulk, or by a retry policy coming due — is held to it. Not enforced by a unique
    /// index as well: exchanges retried before this rule existed can already have forked, and an
    /// index that will not create over the data it inherits is worse than no index. Two retries of
    /// the same exchange committed at the very same moment can therefore still both pass this
    /// check; the loser is a duplicate attempt, which the tree then shows as a fork.
    /// </remarks>
    private async Task EnsureNotAlreadyRetried(string xchangeId)
    {
        var existing = await FindRetryOf(xchangeId);
        if (existing != null)
            throw new SWValidationException("ALREADY_RETRIED",
                $"This exchange has already been retried, as exchange {existing}. Retry that attempt " +
                "instead — an exchange is only retried once, so that its attempts stay a single chain.");
    }

    private async Task<int> CountRetryChainDepth(Xchange xchange)
    {
        var depth = 0;
        var retryFor = xchange.RetryFor;
        while (retryFor != null)
        {
            depth++;
            var parent = await dbContext.Set<Xchange>()
                .AsNoTracking()
                .Where(x => x.Id == retryFor)
                .Select(x => x.RetryFor)
                .FirstOrDefaultAsync();
            retryFor = parent;
        }
        return depth;
    }

    async Task CreateXchangesForHits(Xchange xchange, FilterResult result, XchangeFile inputFile)
    {
        foreach (var subscriptionId in result.Hits)
        {
            var subscription = await BitweenCache.SubscriptionByIdAsync(subscriptionId);
            if (subscription.PausedOn != null)
            {
                await CreateOnHoldXchange(subscription, inputFile);
            }
            else
            {
                await CreateXchange(subscription, inputFile, null, xchange.CorrelationId);
            }
        }

        if (result.GatewayHits.Count == 0)
            return;

        // Bus-gateway routes: run the assigned subscription with the route's optional partner values,
        // reusing the same xchange path the API gateway uses (partner + globals injection).
        var globalAdapterValuesSets = await dbContext.Set<GlobalAdapterValuesSet>().ToArrayAsync();
        foreach (var hit in result.GatewayHits)
        {
            var subscription = await BitweenCache.SubscriptionByIdAsync(hit.SubscriptionId);
            if (subscription == null)
            {
                logger.LogWarning(
                    "Bus gateway route references subscription {SubscriptionId}, which is not active; skipping.",
                    hit.SubscriptionId);
                continue;
            }

            var partner = hit.PartnerId.HasValue
                ? await dbContext.FindAsync<Partner>(hit.PartnerId.Value)
                : null;

            if (subscription.PausedOn != null)
            {
                await CreateOnHoldXchange(subscription, inputFile);
            }
            else
            {
                await CreateXchange(subscription, inputFile, null, xchange.CorrelationId, partner,
                    globalAdapterValuesSets);
            }
        }
    }

    private async Task ProcessResult(XchangeMessage message)
    {
        var notifiers = await BitweenCache.ListNotifiersAsync();

        var xchangeResult = await dbContext.FindAsync<XchangeResult>(message.Id);
        if (xchangeResult == null)
            throw new BitweenException($"Xchange Result '{message.Id}' not found.");
        var xchange = await dbContext.FindAsync<Xchange>(message.Id);
        if (xchange == null)
            throw new BitweenException($"Xchange '{message.Id}' not found.");

        foreach (var notifier in notifiers)
        {
            if (notifier.Inactive || notifier.RunOnSubscriptions is null) continue;

            //review 
            if (notifier.RunOnSubscriptions.All(i => i != xchange!.SubscriptionId))
            {
                continue;
            }

            switch (xchangeResult.Success)
            {
                case true when !xchangeResult.ResponseBad && notifier.RunOnSuccessfulResult:
                case true when xchangeResult.ResponseBad && notifier.RunOnBadResult:
                case false when notifier.RunOnFailedResult:
                    await NotifyResult(notifier, xchangeResult, xchange?.CorrelationId ?? xchange?.Id);
                    break;
            }
        }
    }

    private async Task NotifyResult(Notifier notifier, XchangeResult xchangeResult, string correlationId)
    {
        if (xchangeResult == null) throw new BitweenException($"Xchange Result '{xchangeResult.Id}' not found.");

        if (notifier?.HandlerId == null) return;

        var xchange = await dbContext.FindAsync<Xchange>(xchangeResult.Id);
        var subscription = await BitweenCache.SubscriptionByIdAsync(xchange!.SubscriptionId!.Value);
        var document = await BitweenCache.DocumentByIdAsync(xchange.DocumentId);

        var notificationData = new XchangeResultNotification
        {
            Id = xchangeResult.Id,
            Exception = xchangeResult.Exception,
            Success = xchangeResult.Success,
            FinishedOn = xchangeResult.FinishedOn,
            OutputBad = xchangeResult.OutputBad,
            ResponseBad = xchangeResult.ResponseBad,
            StartedOn = xchange.StartedOn,
            SubscriptionName = subscription.Name,
            SubscriptionId = subscription.Id,
            DocumentName = document.Name,
            DocumentId = document.Id,
            CorrelationId = xchange.CorrelationId
        };

        var handlerProperties = notifier.HandlerProperties.ToDictionary();
        handlerProperties["xchangeid"] = xchangeResult.Id;

        try
        {
            await adapterInvoker.InvokeAsync<XchangeFile>(
                notifier.HandlerId, AdapterRole.Handler, nameof(IInfolinkHandler.Handle),
                new XchangeFile(JsonConvert.SerializeObject(notificationData), xchangeResult.Id),
                handlerProperties, correlationId);

            dbContext.Add(new XchangeNotification(xchangeResult.Id, notifier.Id, notifier.Name));
        }
        catch (Exception ex)
        {
            dbContext.Add(new XchangeNotification(xchangeResult.Id, notifier.Id, notifier.Name, ex.ToString()));
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task Process(SubscriptionUnpausedEvent message)
    {
        var subscription = await BitweenCache.SubscriptionByIdAsync(message.Id);

        if (subscription == null || subscription.Inactive || subscription.PausedOn != null) return;

        var xchangesDetails = await dbContext.Set<OnHoldXchange>().Where(x => x.SubscriptionId == subscription.Id)
            .ToListAsync();

        foreach (var xchangeDetails in xchangesDetails)
        {
            var file = new XchangeFile(xchangeDetails.Data, xchangeDetails.FileName, xchangeDetails.BadData);
            var partner = xchangeDetails.PartnerId.HasValue
                ? await dbContext.FindAsync<Partner>(xchangeDetails.PartnerId.Value)
                : null;
            await CreateXchange(subscription, file, xchangeDetails.References, xchangeDetails.CorrelationId, partner);
            dbContext.Remove(xchangeDetails);
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task<IEnumerable<string>> GetMessageTypeNames()
    {
        var messageTypeNamesWithOptions = await GetMessageTypeNamesWithOptions();
        return messageTypeNamesWithOptions.Keys;
    }

    public Task Process(string messageTypeName, string message)
    {
        var eventMessage = JsonConvert.DeserializeObject<XchangeMessage>(message);

        return messageTypeName.EndsWith(ResultQueueSuffix) ? ProcessResult(eventMessage) : Process(eventMessage);
    }

    public async Task<IDictionary<string, ConsumerOptions>> GetMessageTypeNamesWithOptions()
    {
        // var workgroups = (await BitweenCache.ListWorkGroupsAsync()).ToList();
        var workgroups = await dbContext.Set<WorkGroup>().ToListAsync();
        workgroups.Add(WorkGroup.None);
        var messageTypeNamesWithOptions = new Dictionary<string, ConsumerOptions>();
        foreach (var workGroup in workgroups)
        {
            var messageTypeName = workGroup.GetBusMessageName();
            messageTypeNamesWithOptions[messageTypeName] = new ConsumerOptions()
            {
                Prefetch = workGroup.Options?.RabbitMqOptions?.Prefetch,
                Priority = workGroup.Options?.RabbitMqOptions?.Priority
            };
            var messageTypeNameForResponse = $"{messageTypeName}{ResultQueueSuffix}";
            messageTypeNamesWithOptions[messageTypeNameForResponse] = new ConsumerOptions()
            {
                Prefetch = workGroup.Options?.RabbitMqOptions?.Prefetch,
                Priority = workGroup.Options?.RabbitMqOptions?.Priority
            };
        }

        if (!BitweenSettings.ConsumeLegacyEventMessages) return messageTypeNamesWithOptions;

        messageTypeNamesWithOptions.Add(nameof(ApiXchangeCreatedEvent), new ConsumerOptions() { Priority = 10 });
        messageTypeNamesWithOptions.Add(nameof(InternalXchangeCreatedEvent), new ConsumerOptions());
        messageTypeNamesWithOptions.Add(nameof(ReceivingXchangeCreatedEvent), new ConsumerOptions());
        messageTypeNamesWithOptions.Add(nameof(AggregateXchangeCreatedEvent), new ConsumerOptions());
        messageTypeNamesWithOptions.Add(nameof(XchangeResultCreatedEvent), new ConsumerOptions());
        return messageTypeNamesWithOptions;
    }
}