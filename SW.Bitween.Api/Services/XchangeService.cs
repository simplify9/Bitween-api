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
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.Bus.RabbitMqExtensions;

namespace SW.Bitween;

public class XchangeService :
    // IConsume<ApiXchangeCreatedEvent>,
    // IConsume<InternalXchangeCreatedEvent>,
    // IConsume<AggregateXchangeCreatedEvent>,
    // IConsume<ReceivingXchangeCreatedEvent>,
    // IConsume<XchangeResultCreatedEvent>,
    IConsume<SubscriptionUnpausedEvent>,
    IConsumeExtended

{
    public const string ResultQueueSuffix = "-Result";
    private readonly BitweenOptions _BitweenSettings;
    private readonly BitweenDbContext _dbContext;
    private readonly FilterService _filterService;
    private readonly ICloudFilesService _cloudFiles;
    private readonly IServiceProvider _serviceProvider;
    private readonly IPublish _publish;
    private readonly ILogger _logger;
    private readonly IInfolinkCache _BitweenCache;
    private readonly NativeAdapterDiscoveryService _nativeAdapterDiscovery;
    private readonly AdapterInvoker _adapterInvoker;

    public XchangeService(BitweenOptions BitweenSettings, BitweenDbContext dbContext,
        FilterService filterService,
        ICloudFilesService cloudFiles, IServiceProvider serviceProvider,
        IPublish publish, ILogger<XchangeService> logger, IInfolinkCache BitweenCache,
        NativeAdapterDiscoveryService nativeAdapterDiscovery, AdapterInvoker adapterInvoker)
    {
        _adapterInvoker = adapterInvoker;
        _BitweenSettings = BitweenSettings;
        _dbContext = dbContext;
        _filterService = filterService;
        _cloudFiles = cloudFiles;
        _nativeAdapterDiscovery = nativeAdapterDiscovery;
        _serviceProvider = serviceProvider;
        _publish = publish;
        _logger = logger;
        _BitweenCache = BitweenCache;
    }

    public async Task<string> SubmitSubscriptionXchange(int subscriptionId, XchangeFile file,
        string[] references = null, Partner gatewayPartner = null,
        GlobalAdapterValuesSet[] globalAdapterValuesSets = null)
    {
        var subscription = await _BitweenCache.SubscriptionByIdAsync(subscriptionId);

        var xchange = await CreateXchange(subscription, file, references, Guid.NewGuid().ToString("N"), gatewayPartner,
            globalAdapterValuesSets);
        await _dbContext.SaveChangesAsync();
        return xchange.Id;
    }

    public async Task SubmitFilterXchange(int documentId, XchangeFile file, string[] references = null,
        string correlationId = null)
    {
        var document = await _BitweenCache.DocumentByIdAsync(documentId);
        Xchange xchange;

        if (document?.DisregardsUnfilteredMessages ?? false)
        {
            xchange = new Xchange(documentId, null, file, references, SubscriptionType.Internal, correlationId);
            var result = await _filterService.Filter(xchange.DocumentId, file);
            await CreateXchangesForHits(xchange, result, file);
        }
        else
        {
            xchange = await CreateXchange(document, null, file, references, correlationId);
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task CreateXchange(Xchange xchange, XchangeFile file, WorkGroup workGroup,
        bool manualRetry = false)
    {
        var newXchange = new Xchange(xchange, file, workGroup, manualRetry);
        await AddFile(newXchange.Id, XchangeFileType.Input, file);
        _dbContext.Add(newXchange);
    }

    public async Task CreateXchange(Subscription subscription, Xchange xchange, XchangeFile file,
        string[] references = null, Dictionary<string, int> groupAttemptCounts = null, bool manualRetry = false)
    {
        var partnerId = xchange.PartnerId ?? subscription.PartnerId;
        var partner = partnerId.HasValue ? await _dbContext.FindAsync<Partner>(partnerId.Value) : null;
        var globalAdapterValuesSets = await _BitweenCache.ListGlobalAdapterValuesSetsAsync();
        var newXchange = new Xchange(subscription, xchange, file, partner, globalAdapterValuesSets,
            groupAttemptCounts, manualRetry);
        await AddFile(newXchange.Id, XchangeFileType.Input, file);
        _dbContext.Add(newXchange);
    }

    public async Task<Xchange> CreateXchange(Document document, WorkGroup workGroup, XchangeFile file,
        string[] references = null,
        string correlationId = null)
    {
        var xchange = new Xchange(document.Id, workGroup, file, references, SubscriptionType.Internal, correlationId);
        await AddFile(xchange.Id, XchangeFileType.Input, file);
        _dbContext.Add(xchange);
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
        globalAdapterValuesSets ??= await _BitweenCache.ListGlobalAdapterValuesSetsAsync();
        var xchange = new Xchange(subscription, file, references, correlationId, gatewayPartner,
            globalAdapterValuesSets);
        await AddFile(xchange.Id, XchangeFileType.Input, file);
        _dbContext.Add(xchange);
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
        var xchange = await _dbContext.FindAsync<Xchange>(delayedRetry.Id);
        if (xchange == null)
        {
            _dbContext.Remove(delayedRetry);
            return false;
        }

        var subscription = await _dbContext.Set<Subscription>()
            .FirstOrDefaultAsync(s => s.Id == xchange.SubscriptionId);
        if (subscription == null)
        {
            // Recorded on the result like the unreadable-input case below, rather than only dropping
            // the schedule: the exchange is still there for someone to look at, so leaving it with no
            // reason means the retry simply stopped happening with nothing to explain it.
            _dbContext.Remove(delayedRetry);

            var orphaned = await _dbContext.FindAsync<XchangeResult>(xchange.Id);
            orphaned?.SetRetryBlocked(
                "The scheduled retry was dropped: the subscription it belonged to no longer exists.");
            return false;
        }

        var inputFile = await ReadInputFile(xchange);
        if (inputFile == null)
        {
            // The input is what a retry re-sends, so without it there is nothing to retry with. Handled
            // like a missing subscription — drop the schedule and move on — but recorded on the result
            // as well, because unlike a deleted subscription this needs someone to look into it.
            _dbContext.Remove(delayedRetry);

            var result = await _dbContext.FindAsync<XchangeResult>(xchange.Id);
            result?.SetRetryBlocked("The scheduled retry was dropped: the input file could not be read.");
            return false;
        }

        await CreateXchange(subscription, xchange, inputFile);
        _dbContext.Remove(delayedRetry);
        return true;
    }

    /// <summary>
    /// The original input, or <c>null</c> when it cannot be read — deleted from storage, expired by a
    /// lifecycle rule, or storage itself unavailable.
    /// </summary>
    private async Task<XchangeFile> ReadInputFile(Xchange xchange)
    {
        try
        {
            return new XchangeFile(await GetFile(xchange.Id, XchangeFileType.Input), xchange.InputName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The input file of xchange {XchangeId} could not be read.", xchange.Id);
            return null;
        }
    }

    private Task CreateOnHoldXchange(Subscription subscription, XchangeFile file, string[] references = null)
    {
        var xchange = new OnHoldXchange(subscription, file.Data, file.Filename, file.BadData, references);
        _dbContext.Add(xchange);
        return Task.CompletedTask;
    }


    private async Task<XchangeFile> RunMapper(Xchange xchange, XchangeFile xchangeFile)
    {
        if (xchange.MapperId == null) return xchangeFile;

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
                var partner = await _dbContext.FindAsync<Partner>(xchange.PartnerId.Value);
                if (partner?.AdapterProperties?.Count > 0)
                {
                    jObjEnriched["__partner__"] = JObject.FromObject(partner.AdapterProperties);
                    enriched = true;
                }
            }

            // Inject __globals__ — all global adapter values sets
            // so templates can use {{ __globals__?.setId?.key }}
            var globalSets = await _dbContext.Set<GlobalAdapterValuesSet>().ToListAsync();
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

        var mapperProperties = xchange.MapperProperties.ToDictionary();
        mapperProperties["xchangeid"] = xchange.Id;

        // Check if it's a native adapter
        if (xchange.MapperId.StartsWith(NativeAdapterDiscoveryService.NativePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var handler = _nativeAdapterDiscovery.GetNativeMapper(xchange.MapperId, mapperProperties);
            xchangeFile = await handler.Handle(xchangeFile);
        }
        else
        {
            // Use serverless for external adapters
            var serverless = _serviceProvider.GetRequiredService<IServerlessService>();
            await serverless.StartAsync(xchange.MapperId, xchange.CorrelationId ?? xchange.Id, mapperProperties);
            xchangeFile = await serverless.InvokeAsync<XchangeFile>(nameof(IInfolinkHandler.Handle), xchangeFile);
        }

        if (xchangeFile is null)
            throw new BitweenException(
                $"Unexpected null return value after running mapping for exchange id: {xchange.Id}, adapter id: {xchange.MapperId}");
        else
            await AddFile(xchange.Id, XchangeFileType.Output, xchangeFile);
        return xchangeFile;
    }

    public async Task RunValidator(string validatorId, IDictionary<string, string> properties,
        XchangeFile xchangeFile)
    {
        if (validatorId == null) return;

        InfolinkValidatorResult result;

        // Check if it's a native adapter
        if (validatorId.StartsWith(NativeAdapterDiscoveryService.NativePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var validator = _nativeAdapterDiscovery.GetNativeValidator(validatorId, properties);

            result = await validator.Validate(xchangeFile);
        }
        else
        {
            // Use serverless for external adapters
            var serverless = _serviceProvider.GetRequiredService<IServerlessService>();
            await serverless.StartAsync(validatorId, null, properties);
            result = await serverless.InvokeAsync<InfolinkValidatorResult>(nameof(IInfolinkValidator.Validate),
                xchangeFile);
        }

        if (!result.Success)
            throw new SWValidationException(result.Validations);
    }

    private async Task<XchangeFile> RunHandler(Xchange xchange, XchangeFile xchangeFile)
    {
        if (xchange.HandlerId == null) return null;

        var handlerProperties = xchange.HandlerProperties.ToDictionary();
        handlerProperties["xchangeid"] = xchange.Id;

        // Check if it's a native adapter
        if (xchange.HandlerId.StartsWith(NativeAdapterDiscoveryService.NativePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var handler = _nativeAdapterDiscovery.GetNativeHandler(xchange.HandlerId, handlerProperties);
            xchangeFile = await handler.Handle(xchangeFile);
        }
        else
        {
            // Use serverless for external adapters
            var serverless = _serviceProvider.GetRequiredService<IServerlessService>();
            await serverless.StartAsync(xchange.HandlerId, xchange.CorrelationId ?? xchange.Id, handlerProperties);
            xchangeFile = await serverless.InvokeAsync<XchangeFile>(nameof(IInfolinkHandler.Handle), xchangeFile);
        }

        if (xchangeFile != null)
            await AddFile(xchange.Id, XchangeFileType.Response, xchangeFile);
        return xchangeFile;
    }

    // private T InstantiateNativeAdapter<T>(string adapterId, IDictionary<string, string> properties)
    // {
    //     var adapterInfo = _nativeAdapterDiscovery.GetNativeAdapterInfo(adapterId);
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

    private async Task AddFile(string xchangeId, XchangeFileType type, XchangeFile file)
    {
        await _cloudFiles.WriteTextAsync(file.Data, new WriteFileSettings
        {
            Public = !_BitweenSettings.AreXChangeFilesPrivate,
            Key = GetFileKey(xchangeId, type)
        });
    }

    public string GetFileUrl(string xchangeId, XchangeFileType type)
    {
        return _cloudFiles.GetUrl(GetFileKey(xchangeId, type));
    }

    public string GetFileUrl(string xchangeId, int? fileSize, XchangeFileType type)
    {
        return fileSize is null or 0 ? null : _cloudFiles.GetUrl(GetFileKey(xchangeId, type));
    }

    public string GetFileKey(string xchangeId, int? fileSize, XchangeFileType type)
    {
        if (fileSize is null or 0)
            return null;
        var key = $"{_BitweenSettings.DocumentPrefix}/{xchangeId}/{type.ToString().ToLower()}";
        _logger.LogInformation($"the file key is:'{key}'");
        return key;
    }

    private string GetFileKey(string xchangeId, XchangeFileType type)
    {
        var key = $"{_BitweenSettings.DocumentPrefix}/{xchangeId}/{type.ToString().ToLower()}";
        _logger.LogInformation($"the file key is:'{key}'");
        return key;
    }

    public async Task<string> GetFile(string xchangeId, XchangeFileType type)
    {
        await using var cloudStream = await _cloudFiles.OpenReadAsync(GetFileKey(xchangeId, type));
        using var reader = new StreamReader(cloudStream);
        return await reader.ReadToEndAsync();
    }

    private async Task Process(XchangeMessage message)
    {
        Xchange responseXchange = null;
        XchangeFile outputFile = null;
        XchangeFile responseFile = null;
        WorkGroup workGroup = null;
        var xchange = await _dbContext.FindAsync<Xchange>(message.Id);

        if (xchange == null) throw new BitweenException($"Xchange '{message.Id}' not found.");

        try
        {
            var inputFile = new XchangeFile(await GetFile(xchange.Id, XchangeFileType.Input), xchange.InputName);
            var result = await _filterService.Filter(xchange.DocumentId, inputFile);

            _dbContext.Add(new XchangePromotedProperties(xchange.Id, result));

            if (xchange.SubscriptionId != null)
            {
                workGroup = await _BitweenCache.WorkGroupBySubscriptionIdAsync(xchange.SubscriptionId.Value);
                if (xchange.MapperId == null)
                    responseFile = await RunHandler(xchange, inputFile);
                else
                {
                    outputFile = await RunMapper(xchange, inputFile);
                    responseFile = await RunHandler(xchange, outputFile);
                }

                if (xchange.ResponseSubscriptionId != null && responseFile != null)
                {
                    var subscription =
                        await _BitweenCache.SubscriptionByIdAsync(xchange.ResponseSubscriptionId.Value);

                    responseXchange = await CreateXchange(subscription, responseFile, null, xchange.CorrelationId);
                }

                if (!string.IsNullOrWhiteSpace(xchange.ResponseMessageTypeName) && responseFile != null &&
                    !responseFile.BadData)
                {
                    await _publish.Publish(xchange.ResponseMessageTypeName, responseFile.Data);
                }
            }
            else if (xchange.SubscriptionId == null)
            {
                await CreateXchangesForHits(xchange, result, inputFile);
            }

            var xchangeResult = new XchangeResult(xchange.Id, workGroup, outputFile, responseFile,
                responseXchange?.Id);
            _dbContext.Add(xchangeResult);
            if (responseFile?.BadData == true)
                await TrySchedulingWithoutLosingTheResult(xchange, XchangeResultType.BadResult, responseFile.Data,
                    xchangeResult);
            else
                await TryClearingRetryBudgetAfterSuccess(xchange);
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            var xchangeResult = new XchangeResult(xchange.Id, workGroup, outputFile, responseFile,
                responseXchange?.Id, ex.ToString());
            _dbContext.Add(xchangeResult);
            await TrySchedulingWithoutLosingTheResult(xchange, XchangeResultType.Error, ex.ToString(), xchangeResult);
            await _dbContext.SaveChangesAsync();
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
            _logger.LogError(ex, "Auto-retry evaluation failed for xchange {XchangeId}; the failure result is still recorded.",
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
            await new RetryGroupBudget(_dbContext, _serviceProvider, xchange.SubscriptionId.Value)
                .ReleaseExhaustedBudgets(xchange.StartedOn);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
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
        var alreadyScheduled = await _dbContext.Set<DelayedRetry>().FindAsync(xchange.Id);
        if (alreadyScheduled != null) return;

        var subscription = await _dbContext.Set<Subscription>()
            .Include(s => s.RetryPolicy)
            .FirstOrDefaultAsync(s => s.Id == xchange.SubscriptionId.Value);

        IRetryPolicy policy = subscription?.CustomRetryPolicy ?? (IRetryPolicy)subscription?.RetryPolicy;
        if (policy?.Groups == null || policy.Groups.Count == 0) return;

        var evaluator = new RetryPolicyEvaluator(policy,
            new RetryGroupBudget(_dbContext, _serviceProvider, xchange.SubscriptionId.Value));

        var attemptIndex = await CountRetryChainDepth(xchange);
        var decision = await evaluator.Evaluate(resultType, content, attemptIndex);

        // Which group owned this failure, so the group's retries can later be listed without
        // re-deriving the match, and how deep the chain already was without walking it again.
        if (decision.MatchedGroup is not null)
            xchangeResult.SetRetryEvaluation(decision.MatchedGroup.Id, attemptIndex);

        if (decision.ShouldRetry)
            _dbContext.Add(new DelayedRetry
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
                decision.MatchedGroup.Budget!.MaxAttemptsTotal);
    }

    private async Task<int> CountRetryChainDepth(Xchange xchange)
    {
        var depth = 0;
        var retryFor = xchange.RetryFor;
        while (retryFor != null)
        {
            depth++;
            var parent = await _dbContext.Set<Xchange>()
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
            var subscription = await _BitweenCache.SubscriptionByIdAsync(subscriptionId);
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
        var globalAdapterValuesSets = await _dbContext.Set<GlobalAdapterValuesSet>().ToArrayAsync();
        foreach (var hit in result.GatewayHits)
        {
            var subscription = await _BitweenCache.SubscriptionByIdAsync(hit.SubscriptionId);
            if (subscription == null)
            {
                _logger.LogWarning(
                    "Bus gateway route references subscription {SubscriptionId}, which is not active; skipping.",
                    hit.SubscriptionId);
                continue;
            }

            var partner = hit.PartnerId.HasValue
                ? await _dbContext.FindAsync<Partner>(hit.PartnerId.Value)
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
        var notifiers = await _BitweenCache.ListNotifiersAsync();

        var xchangeResult = await _dbContext.FindAsync<XchangeResult>(message.Id);
        if (xchangeResult == null)
            throw new BitweenException($"Xchange Result '{message.Id}' not found.");
        var xchange = await _dbContext.FindAsync<Xchange>(message.Id);
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

        var xchange = await _dbContext.FindAsync<Xchange>(xchangeResult.Id);
        var subscription = await _BitweenCache.SubscriptionByIdAsync(xchange!.SubscriptionId!.Value);
        var document = await _BitweenCache.DocumentByIdAsync(xchange.DocumentId);

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
            await _adapterInvoker.Handle(notifier.HandlerId, handlerProperties, correlationId,
                new XchangeFile(JsonConvert.SerializeObject(notificationData), xchangeResult.Id));

            _dbContext.Add(new XchangeNotification(xchangeResult.Id, notifier.Id, notifier.Name));
        }
        catch (Exception ex)
        {
            _dbContext.Add(new XchangeNotification(xchangeResult.Id, notifier.Id, notifier.Name, ex.ToString()));
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task Process(SubscriptionUnpausedEvent message)
    {
        var subscription = await _BitweenCache.SubscriptionByIdAsync(message.Id);

        if (subscription == null || subscription.Inactive || subscription.PausedOn != null) return;

        var xchangesDetails = await _dbContext.Set<OnHoldXchange>().Where(x => x.SubscriptionId == subscription.Id)
            .ToListAsync();

        foreach (var xchangeDetails in xchangesDetails)
        {
            var file = new XchangeFile(xchangeDetails.Data, xchangeDetails.FileName, xchangeDetails.BadData);
            await CreateXchange(subscription, file, xchangeDetails.References);
            _dbContext.Remove(xchangeDetails);
        }

        await _dbContext.SaveChangesAsync();
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
        // var workgroups = (await _BitweenCache.ListWorkGroupsAsync()).ToList();
        var workgroups = await _dbContext.Set<WorkGroup>().ToListAsync();
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

        if (!_BitweenSettings.ConsumeLegacyEventMessages) return messageTypeNamesWithOptions;

        messageTypeNamesWithOptions.Add(nameof(ApiXchangeCreatedEvent), new ConsumerOptions() { Priority = 10 });
        messageTypeNamesWithOptions.Add(nameof(InternalXchangeCreatedEvent), new ConsumerOptions());
        messageTypeNamesWithOptions.Add(nameof(ReceivingXchangeCreatedEvent), new ConsumerOptions());
        messageTypeNamesWithOptions.Add(nameof(AggregateXchangeCreatedEvent), new ConsumerOptions());
        messageTypeNamesWithOptions.Add(nameof(XchangeResultCreatedEvent), new ConsumerOptions());
        return messageTypeNamesWithOptions;
    }
}