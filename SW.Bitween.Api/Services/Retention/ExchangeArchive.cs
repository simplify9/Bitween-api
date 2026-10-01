using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween;

/// <summary>
/// Writes exchanges to the archive before the retention job deletes them: one JSON file per exchange
/// holding its details and, while storage still has them, its files' contents.
/// </summary>
/// <remarks>
/// <para>
/// Files go to <c>{archive prefix}/{subscription id}/yyyy/MM/dd/{first promoted value}_{exchange id}.json</c>,
/// dated by when the exchange started. The first promoted property of the exchange's information type
/// leads the name so an exchange can be found by it; the id keeps two with the same value — the
/// attempts of one retry chain — from overwriting each other. Rewriting the same exchange produces the
/// same key, so a run that stopped part-way can simply run again.
/// </para>
/// <para>
/// Handler and mapper properties are left out on purpose: they hold resolved partner values, secrets
/// included, and an archive is read far more widely than the exchange row ever was.
/// </para>
/// </remarks>
public class ExchangeArchive(BitweenDbContext dbContext, XchangeService xchangeService, ICloudFilesService cloudFiles,
    StorageRetention storageRetention, BitweenOptions options, ILogger<ExchangeArchive> logger)
{
    /// <summary>How many exchanges have their files read at once.</summary>
    private const int ParallelReads = 8;

    private static readonly JsonSerializerSettings Json = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver { NamingStrategy = { ProcessDictionaryKeys = false } },
        NullValueHandling = NullValueHandling.Include
    };

    /// <summary>Where archives go: the document prefix with its <c>tempN/</c> part swapped for <c>archive/</c>.</summary>
    public string Prefix => PrefixFor(options.DocumentPrefix);

    /// <summary>
    /// Out of the temp folders on purpose — the bucket's rule for <c>temp30/</c> would otherwise delete
    /// the archive along with the files it keeps. The rest of the prefix stays, so deployments that share
    /// a bucket under different prefixes keep separate archives.
    /// </summary>
    public static string PrefixFor(string documentPrefix)
    {
        var rest = Regex.Replace(documentPrefix ?? string.Empty, @"^temp\d+(/|$)", string.Empty);
        return rest.Length == 0 ? "archive" : $"archive/{rest}";
    }

    public static string KeyFor(string prefix, Xchange xchange, string mainValue) =>
        $"{prefix}/{xchange.SubscriptionId?.ToString(CultureInfo.InvariantCulture) ?? "no-subscription"}/" +
        $"{xchange.StartedOn.ToString("yyyy'/'MM'/'dd", CultureInfo.InvariantCulture)}/{FileName(mainValue, xchange.Id)}";

    /// <summary>The main value made safe for a storage key, then the id; the id alone when there's no value.</summary>
    public static string FileName(string mainValue, string xchangeId)
    {
        var safe = Regex.Replace(mainValue ?? string.Empty, "[^A-Za-z0-9._-]+", "-").Trim('-', '.');
        if (safe.Length > 80) safe = safe[..80].TrimEnd('-', '.');
        return safe.Length == 0 ? $"{xchangeId}.json" : $"{safe}_{xchangeId}.json";
    }

    /// <summary>
    /// Archives <paramref name="xchanges"/> and returns the ones written. Stops after the first group
    /// with a failure, so the caller only ever deletes exchanges that made it into the archive.
    /// </summary>
    public async Task<IReadOnlyList<Xchange>> WriteAsync(IReadOnlyList<Xchange> xchanges)
    {
        var ids = xchanges.Select(x => x.Id).ToList();
        var results = await dbContext.Set<XchangeResult>().AsNoTracking()
            .Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id);
        var promoted = await dbContext.Set<XchangePromotedProperties>().AsNoTracking()
            .Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var aggregations = await dbContext.Set<XchangeAggregation>().AsNoTracking()
            .Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id);
        var notifications = (await dbContext.Set<XchangeNotification>().AsNoTracking()
            .Where(n => ids.Contains(n.XchangeId)).ToListAsync()).ToLookup(n => n.XchangeId);

        var documentIds = xchanges.Select(x => x.DocumentId).Distinct().ToList();
        var documents = await dbContext.Set<Document>().AsNoTracking()
            .Where(d => documentIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id);
        var subscriptionIds = xchanges.Where(x => x.SubscriptionId != null).Select(x => x.SubscriptionId!.Value)
            .Distinct().ToList();
        var subscriptionNames = await dbContext.Set<Subscription>().AsNoTracking()
            .Where(s => subscriptionIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name);

        var rules = await storageRetention.GetAsync();
        var prefix = Prefix;
        var written = new List<Xchange>();

        foreach (var group in xchanges.Chunk(ParallelReads))
        {
            // The files are read in parallel; everything from the database was loaded above, because a
            // DbContext can't be shared between concurrent calls.
            var outcomes = await Task.WhenAll(group.Select(async xchange =>
            {
                try
                {
                    documents.TryGetValue(xchange.DocumentId, out var document);
                    results.TryGetValue(xchange.Id, out var result);
                    aggregations.TryGetValue(xchange.Id, out var aggregation);
                    var values = promoted.GetValueOrDefault(xchange.Id)?.Properties
                        .InDefinedOrder(document?.PromotedProperties);

                    var archived = new
                    {
                        xchange.Id,
                        Status = result == null ? "Running" : !result.Success ? "Failed" : result.ResponseBad ? "BadResponse" : "Succeeded",
                        xchange.StartedOn,
                        FinishedOn = result?.FinishedOn,
                        Subscription = xchange.SubscriptionId == null
                            ? null
                            : new { Id = xchange.SubscriptionId, Name = subscriptionNames.GetValueOrDefault(xchange.SubscriptionId.Value) },
                        InformationType = new { Id = xchange.DocumentId, document?.Name },
                        xchange.PartnerId,
                        xchange.CorrelationId,
                        xchange.References,
                        xchange.RetryFor,
                        xchange.ManualRetry,
                        xchange.MapperId,
                        xchange.HandlerId,
                        xchange.ResponseSubscriptionId,
                        xchange.ResponseMessageTypeName,
                        PromotedProperties = values,
                        Result = result == null
                            ? null
                            : new
                            {
                                result.Success,
                                result.Exception,
                                result.OutputBad,
                                result.ResponseBad,
                                result.ResponseXchangeId,
                                result.RetryBlockedReason,
                                result.RetryGroupId,
                                result.AttemptNumber
                            },
                        Aggregation = aggregation == null
                            ? null
                            : new { aggregation.AggregationXchangeId, aggregation.AggregatedOn },
                        Notifications = notifications[xchange.Id].Select(n => new
                        {
                            n.NotifierId,
                            n.NotifierName,
                            n.Success,
                            n.Exception,
                            n.FinishedOn
                        }),
                        Files = new
                        {
                            Input = await ReadFile(xchange, XchangeFileType.Input, xchange.InputName,
                                xchange.InputSize, xchange.InputContentType, rules),
                            Output = result == null
                                ? null
                                : await ReadFile(xchange, XchangeFileType.Output, result.OutputName,
                                    result.OutputSize, result.OutputContentType, rules),
                            Response = result == null
                                ? null
                                : await ReadFile(xchange, XchangeFileType.Response, result.ResponseName,
                                    result.ResponseSize, result.ResponseContentType, rules)
                        },
                        ArchivedOn = DateTime.UtcNow
                    };

                    var mainProperty = document?.PromotedProperties?.Keys.FirstOrDefault();
                    var mainValue = mainProperty == null ? null : values?.GetValueOrDefault(mainProperty);

                    await cloudFiles.WriteTextAsync(JsonConvert.SerializeObject(archived, Json), new WriteFileSettings
                    {
                        Key = KeyFor(prefix, xchange, mainValue),
                        ContentType = "application/json"
                    });
                    return true;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Could not archive xchange {XchangeId}; it is kept until a later run archives it.",
                        xchange.Id);
                    return false;
                }
            }));

            written.AddRange(group.Where((_, i) => outcomes[i]));
            if (outcomes.Any(ok => !ok)) break;
        }

        return written;
    }

    /// <summary>
    /// One of the exchange's files as it goes into the archive. A file the bucket has already deleted is
    /// recorded as missing, with the reason, rather than stopping the archive: its details are still worth
    /// keeping. Any other storage failure does stop it.
    /// </summary>
    private async Task<object> ReadFile(Xchange xchange, XchangeFileType type, string name, int size,
        string contentType, StorageRules rules)
    {
        if (size == 0) return null;

        var key = xchangeService.FileKey(xchange, type);
        try
        {
            await using var stream = await cloudFiles.OpenReadAsync(key);
            using var reader = new StreamReader(stream);
            return new { Name = name, ContentType = contentType, Size = size, Content = await reader.ReadToEndAsync() };
        }
        catch (Exception ex) when (StorageErrors.IsNotFound(ex))
        {
            var rule = rules.RuleFor(key);
            return new
            {
                Name = name,
                ContentType = contentType,
                Size = size,
                Content = (string)null,
                Missing = rule != null
                    ? $"Deleted by the storage retention policy before the exchange was archived: files under {rule.Prefix} are kept {rule.Days} days."
                    : "Not in storage when the exchange was archived."
            };
        }
    }
}
