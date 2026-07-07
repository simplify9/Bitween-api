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

    public XchangeService(BitweenOptions BitweenSettings, BitweenDbContext dbContext,
        FilterService filterService,
        ICloudFilesService cloudFiles, IServiceProvider serviceProvider,
        IPublish publish, ILogger<XchangeService> logger, IInfolinkCache BitweenCache,
        NativeAdapterDiscoveryService nativeAdapterDiscovery)
    {
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

    public async Task CreateXchange(Xchange xchange, XchangeFile file, WorkGroup workGroup, Dictionary<string, int> groupAttemptCounts = null)
    {
        var newXchange = new Xchange(xchange, file, workGroup, groupAttemptCounts);
        await AddFile(newXchange.Id, XchangeFileType.Input, file);
        _dbContext.Add(newXchange);
    }

    public async Task CreateXchange(Subscription subscription, Xchange xchange, XchangeFile file,
        string[] references = null, Dictionary<string, int> groupAttemptCounts = null)
    {
        var newXchange = new Xchange(subscription, xchange, file, groupAttemptCounts);
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
            _dbContext.Remove(delayedRetry);
            return false;
        }

        var inputFileData = await GetFile(xchange.Id, XchangeFileType.Input);
        var inputFile = new XchangeFile(inputFileData, xchange.InputName);
        await CreateXchange(subscription, xchange, inputFile, groupAttemptCounts: delayedRetry.GroupAttemptCounts);
        _dbContext.Remove(delayedRetry);
        return true;
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

            _dbContext.Add(new XchangeResult(xchange.Id, workGroup, outputFile, responseFile, responseXchange?.Id));
            if (responseFile?.BadData == true)
                await TryScheduleAutoRetry(xchange, XchangeResultType.BadResult, responseFile.Data);
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _dbContext.Add(new XchangeResult(xchange.Id, workGroup, outputFile, responseFile, responseXchange?.Id,
                ex.ToString()));
            await TryScheduleAutoRetry(xchange, XchangeResultType.Error, ex.ToString());
            await _dbContext.SaveChangesAsync();
        }
    }

    private async Task TryScheduleAutoRetry(Xchange xchange, XchangeResultType resultType, string content)
    {
        if (xchange.SubscriptionId == null) return;

        var subscription = await _dbContext.Set<Subscription>()
            .Include(s => s.RetryPolicy)
            .FirstOrDefaultAsync(s => s.Id == xchange.SubscriptionId.Value);

        IRetryPolicy policy = subscription?.CustomRetryPolicy ?? (IRetryPolicy)subscription?.RetryPolicy;
        if (policy?.Groups == null || policy.Groups.Count == 0) return;

        var evaluator = new RetryPolicyEvaluator(policy);
        evaluator.RestoreGroupAttemptCounts(xchange.GroupAttemptCounts ?? new Dictionary<string, int>());

        var attemptIndex = await CountRetryChainDepth(xchange);
        var decision = evaluator.Evaluate(resultType, content, attemptIndex);

        if (decision.ShouldRetry)
        {
            _dbContext.Add(new DelayedRetry
            {
                Id = xchange.Id,
                On = DateTime.UtcNow + decision.Delay,
                GroupAttemptCounts = evaluator.GetGroupAttemptCounts()
            });
        }
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
            // Check if it's a native adapter
            if (notifier.HandlerId.StartsWith(NativeAdapterDiscoveryService.NativePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var handler = _nativeAdapterDiscovery.GetNativeHandler(notifier.HandlerId, handlerProperties);
                await handler.Handle(new XchangeFile(JsonConvert.SerializeObject(notificationData), xchangeResult.Id));
            }
            else
            {
                // Use serverless for external adapters
                var serverless = _serviceProvider.GetRequiredService<IServerlessService>();
                await serverless.StartAsync(notifier.HandlerId, correlationId, handlerProperties);
                await serverless.InvokeAsync<XchangeFile>(nameof(IInfolinkHandler.Handle),
                    new XchangeFile(JsonConvert.SerializeObject(notificationData), xchangeResult.Id));
            }

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