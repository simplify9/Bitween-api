#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xchanges = SW.Bitween.Resources.Xchanges;
using DelayedRetries = SW.Bitween.Resources.DelayedRetries;
using Ops = SW.Bitween.Resources.Ops;
using Subscriptions = SW.Bitween.Resources.Subscriptions;
using Partners = SW.Bitween.Resources.Partners;

namespace SW.Bitween.Services.Mcp;

/// <summary>
/// What an AI app connected at /mcp can do: look at exchanges, retries, queues, subscriptions and
/// partners, and retry exchanges or pause subscriptions. Each tool calls the handler behind the same
/// screen, in this request's scope, so it answers with the API's own rows and checks the caller's
/// permissions the way the screen does - nothing here decides what anyone may see.
/// </summary>
/// <remarks>
/// Searches go through the same query strings the API takes. Search rows drop their file links: those
/// open without signing in, and an AI app's transcript is no place for one.
/// </remarks>
[McpServerToolType]
public class BitweenTools(ApiJson json)
{
    const int MaxPageSize = 100;
    const int DefaultFileCharacters = 20_000;
    const int MaxFileCharacters = 200_000;

    // --- Exchanges ---

    [McpServerTool(Name = "search_exchanges", Title = "Search exchanges", ReadOnly = true, OpenWorld = false)]
    [Description("Searches exchanges - documents Bitween received and delivered - newest first, filtered as the Exchanges page filters them. Answers with one page of rows and how many match (counting stops past 10,000). Leave a filter out to match any.")]
    public Task<string> SearchExchanges(
        Xchanges.Search search,
        [Description("processing, success, badResponse or failed.")] string? status = null,
        [Description("Started at or after this time, ISO 8601 with an offset.")] DateTimeOffset? from = null,
        [Description("Started at or before this time, ISO 8601 with an offset.")] DateTimeOffset? to = null,
        [Description("Any of these partners' ids.")] int[]? partnerIds = null,
        [Description("Run by any of these subscriptions' ids.")] int[]? subscriptionIds = null,
        [Description("Of any of these information types' ids.")] int[]? informationTypeIds = null,
        [Description("These exchange ids, with each one's retries.")] string[]? ids = null,
        string? correlationId = null,
        [Description("Only the newest attempt of each retry chain.")] bool latestOnly = false,
        [Description("The error contains this text, ignoring case.")] string? errorContains = null,
        [Description("Promoted properties by key: the value contains the text, or an empty text for 'has the key'. All must hold.")] Dictionary<string, string?>? promotedProperties = null,
        [Description("True for retries only, false for first attempts only.")] bool? isRetry = null,
        [Description("True for those with an automatic retry waiting, false for those without.")] bool? hasScheduledRetry = null,
        [Description("Page number, from 0.")] int page = 0,
        [Description("Rows per page, 1 to 100.")] int pageSize = 25) =>
        Answer(async () =>
        {
            var filters = ExchangeFilters(status, from, to, partnerIds, subscriptionIds, informationTypeIds, ids,
                correlationId, latestOnly, errorContains, promotedProperties, isRetry, hasScheduledRetry);
            var rows = (SearchyResponse<XchangeRow>)await search.Handle(Searchy(filters, page, pageSize, count: true));
            foreach (var row in rows.Result) WithoutFileLinks(row);
            return rows;
        });

    [McpServerTool(Name = "summarize_exchanges", Title = "Count exchanges by status", ReadOnly = true, OpenWorld = false)]
    [Description("How many exchanges match, by status, with the same filters as search_exchanges. Counts on a busy Bitween can come back null when they take too long; narrow the filters then.")]
    public Task<string> SummarizeExchanges(
        Xchanges.Summary summary,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int[]? partnerIds = null,
        int[]? subscriptionIds = null,
        int[]? informationTypeIds = null,
        bool latestOnly = false,
        string? errorContains = null) =>
        Answer(() => summary.Handle(Searchy(ExchangeFilters(null, from, to, partnerIds, subscriptionIds, informationTypeIds,
            null, null, latestOnly, errorContains, null, null, null), 0, 0, count: false)));

    [McpServerTool(Name = "get_exchange", Title = "Get an exchange", ReadOnly = true, OpenWorld = false)]
    [Description("One exchange by id, with its whole retry chain, oldest attempt first. Use read_exchange_file for its files.")]
    public Task<string> GetExchange(Xchanges.Search search, Xchanges.RetryTree retryTree, string id) =>
        Answer(async () =>
        {
            var exchange = await FindExchange(search, id);
            var chain = await retryTree.Handle(new XchangeRetryTreeRequest { Id = exchange.Id });
            return new { Exchange = exchange, RetryChain = chain };
        });

    [McpServerTool(Name = "read_exchange_file", Title = "Read an exchange's file", ReadOnly = true, OpenWorld = false)]
    [Description("The text of one of an exchange's files: the input it received, the output it delivered, or the response the destination answered with. Long files are cut at maxCharacters, and the answer says so.")]
    public Task<string> ReadExchangeFile(
        Xchanges.Search search,
        SW.Bitween.Resources.BitweenDocs.Get documents,
        string id,
        [Description("input, output or response.")] string file,
        [Description("At most this many characters, up to 200,000.")] int maxCharacters = DefaultFileCharacters) =>
        Answer(async () =>
        {
            var exchange = await FindExchange(search, id);
            var key = file?.Trim().ToLowerInvariant() switch
            {
                "input" => exchange.InputKey,
                "output" => exchange.OutputKey,
                "response" => exchange.ResponseKey,
                _ => throw new McpException("file is input, output or response."),
            };
            if (key is null) throw new McpException($"Exchange {exchange.Id} has no {file} file.");

            var text = (string?)json.ToToken(await documents.Handle(new GetBitweenDocModel { DocumentKey = key }))["data"] ?? "";
            var limit = Math.Clamp(maxCharacters, 1, MaxFileCharacters);
            return new
            {
                File = file.Trim().ToLowerInvariant(),
                exchange.Id,
                Characters = text.Length,
                Truncated = text.Length > limit,
                Text = text.Length > limit ? text[..limit] : text,
            };
        });

    [McpServerTool(Name = "retry_exchange", Title = "Retry an exchange", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Runs a finished exchange again, as Retry on the Exchanges page does, and answers with the new attempt's id. Refused for an exchange already retried (retry its newest attempt) or with an automatic retry waiting (use run_scheduled_retry_now).")]
    public Task<string> RetryExchange(
        Xchanges.Retry retry,
        Xchanges.Search search,
        string id,
        [Description("Why, for the audit trail.")] string reason = "Retried from an AI app",
        [Description("Start again from the beginning rather than from where it failed.")] bool reset = false) =>
        Answer(async () =>
        {
            await retry.Handle(id, new XchangeRetry { Reason = reason, Reset = reset });
            var attempt = (SearchyResponse<XchangeRow>)await search.Handle(
                Searchy(XchangeFilter.Build(retryFor: id), 0, 1, count: false));
            return new { Retried = id, NewAttempt = attempt.Result.FirstOrDefault()?.Id };
        });

    [McpServerTool(Name = "preview_retry_exchanges", Title = "Preview retrying many exchanges", ReadOnly = true, OpenWorld = false)]
    [Description("What retry_exchanges would do with these filters - how many it would retry, and which it would skip and why - without retrying anything. Run it first.")]
    public Task<string> PreviewRetryExchanges(
        Xchanges.BulkRetryPreview preview,
        string? status = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int[]? partnerIds = null,
        int[]? subscriptionIds = null,
        int[]? informationTypeIds = null,
        string[]? ids = null,
        string? errorContains = null) =>
        Answer(() => preview.Handle(BulkRetry(status, from, to, partnerIds, subscriptionIds, informationTypeIds, ids,
            errorContains, "Preview", false)));

    [McpServerTool(Name = "retry_exchanges", Title = "Retry many exchanges", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Retries every exchange these filters match - at most 500; narrow the filters or use the Exchanges page for more. Preview with preview_retry_exchanges first.")]
    public Task<string> RetryExchanges(
        Xchanges.BulkRetry bulkRetry,
        string? status = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int[]? partnerIds = null,
        int[]? subscriptionIds = null,
        int[]? informationTypeIds = null,
        string[]? ids = null,
        string? errorContains = null,
        [Description("Why, for the audit trail.")] string reason = "Retried from an AI app",
        bool reset = false) =>
        Answer(() => bulkRetry.Handle(BulkRetry(status, from, to, partnerIds, subscriptionIds, informationTypeIds, ids,
            errorContains, reason, reset)));

    // --- Scheduled retries ---

    [McpServerTool(Name = "list_scheduled_retries", Title = "List scheduled retries", ReadOnly = true, OpenWorld = false)]
    [Description("Automatic retries waiting to run, soonest first, as the Scheduled retries page lists them.")]
    public Task<string> ListScheduledRetries(
        DelayedRetries.Search search,
        int? subscriptionId = null,
        int? informationTypeId = null,
        [Description("The error of the attempt it follows contains this text.")] string? errorContains = null,
        [Description("Due at or after this time.")] DateTimeOffset? from = null,
        [Description("Due at or before this time.")] DateTimeOffset? to = null,
        int page = 0,
        int pageSize = 25) =>
        Answer(() => search.Handle(Searchy(XchangeFilter.ScheduledRetries(subscriptionId, informationTypeId, errorContains, from, to),
            page, pageSize, count: true)));

    [McpServerTool(Name = "run_scheduled_retry_now", Title = "Run a scheduled retry now", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Runs an exchange's waiting automatic retry now instead of at its time.")]
    public Task<string> RunScheduledRetryNow(DelayedRetries.RunNow runNow, [Description("The exchange the retry follows.")] string exchangeId) =>
        Answer(async () => await runNow.Handle(exchangeId, new DelayedRetryRunNow()) ?? new { Ran = exchangeId });

    // --- Health ---

    [McpServerTool(Name = "queue_health", Title = "Queue health", ReadOnly = true, OpenWorld = false)]
    [Description("The message queues behind Bitween, as the Queue health page shows them: totals, alerts, dead letters and queues nothing reads. Each part says why when it couldn't be read, such as RabbitMQ management not being set up.")]
    public Task<string> QueueHealth(Ops.Summary summary, Ops.Alerts alerts, Ops.DeadLetters deadLetters, Ops.UnattendedQueues unattended) =>
        Answer(async () => new
        {
            Summary = await Part(summary.Handle),
            Alerts = await Part(alerts.Handle),
            DeadLetters = await Part(deadLetters.Handle),
            UnattendedQueues = await Part(unattended.Handle),
        });

    [McpServerTool(Name = "dashboard", Title = "Dashboard", ReadOnly = true, OpenWorld = false)]
    [Description("The Dashboard's numbers: traffic and success over the last day and week, the busiest and most failing subscriptions, failures waiting to be acted on, and how retries are going.")]
    public Task<string> Dashboard(SW.Bitween.Resources.Metrics.Overview overview,
        SW.Bitween.Resources.Dashboard.FailuresToActOn failures, SW.Bitween.Resources.Dashboard.RetrySummary retries) =>
        Answer(async () => new
        {
            Traffic = await Part(overview.Handle),
            FailuresToActOn = await Part(failures.Handle),
            Retries = await Part(retries.Handle),
        });

    // --- Subscriptions and partners ---

    [McpServerTool(Name = "list_subscriptions", Title = "List subscriptions", ReadOnly = true, OpenWorld = false)]
    [Description("Subscriptions - what Bitween does with each information type it receives - as the Subscriptions page lists them.")]
    public Task<string> ListSubscriptions(
        Subscriptions.Search search,
        [Description("The name contains this text.")] string? name = null,
        int? informationTypeId = null,
        int? partnerId = null,
        int page = 0,
        int pageSize = 25) =>
        Answer(() =>
        {
            var filters = new List<string>();
            if (!string.IsNullOrWhiteSpace(name)) filters.Add($"Name:4:{name.Trim()}");
            if (informationTypeId != null) filters.Add($"DocumentId:1:{informationTypeId}");
            if (partnerId != null) filters.Add($"PartnerId:1:{partnerId}");
            return search.Handle(Searchy(filters, page, pageSize, count: true));
        });

    [McpServerTool(Name = "get_subscription", Title = "Get a subscription", ReadOnly = true, OpenWorld = false)]
    [Description("One subscription's whole setup: its adapters, mapping, filters, schedule and retry policy, and whether it's paused. Private adapter settings are hidden.")]
    public Task<string> GetSubscription(Subscriptions.Get get, int id) => Answer(() => get.Handle(id));

    [McpServerTool(Name = "pause_subscription", Title = "Pause a subscription", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Pauses a subscription: what arrives for it waits instead of running, until it's resumed.")]
    public Task<string> PauseSubscription(Subscriptions.Pause pause, int id) =>
        Answer(() => SetPaused(pause, id, true));

    [McpServerTool(Name = "resume_subscription", Title = "Resume a subscription", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Resumes a paused subscription: what waited for it runs.")]
    public Task<string> ResumeSubscription(Subscriptions.Pause pause, int id) =>
        Answer(() => SetPaused(pause, id, false));

    [McpServerTool(Name = "list_partners", Title = "List partners", ReadOnly = true, OpenWorld = false)]
    [Description("Partners - the systems Bitween exchanges documents with - as the Partners page lists them.")]
    public Task<string> ListPartners(
        Partners.Search search,
        [Description("The name contains this text.")] string? name = null,
        int page = 0,
        int pageSize = 25) =>
        Answer(() => search.Handle(Searchy(string.IsNullOrWhiteSpace(name) ? [] : [$"Name:4:{name.Trim()}"], page, pageSize, count: true)));

    [McpServerTool(Name = "get_partner", Title = "Get a partner", ReadOnly = true, OpenWorld = false)]
    [Description("One partner's settings and keys. Secret values and keys are hidden.")]
    public Task<string> GetPartner(Partners.Get get, int id) =>
        Answer(async () => await get.Handle(id) ?? throw new McpException($"Bitween has no partner {id}."));

    // --- Helpers ---

    static List<string> ExchangeFilters(string? status, DateTimeOffset? from, DateTimeOffset? to, int[]? partnerIds,
        int[]? subscriptionIds, int[]? informationTypeIds, string[]? ids, string? correlationId, bool latestOnly,
        string? errorContains, Dictionary<string, string?>? promotedProperties, bool? isRetry, bool? hasScheduledRetry) =>
        XchangeFilter.Build(Status(status), from, to, partnerIds, subscriptionIds, informationTypeIds, ids, null,
            correlationId, latestOnly, errorContains, promotedProperties, isRetry, hasScheduledRetry);

    static int? Status(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "processing" => 0,
        "success" => 1,
        "badresponse" or "bad response" => 2,
        "failed" => 3,
        _ => throw new McpException("status is processing, success, badResponse or failed."),
    };

    /// <summary>A search request as the API builds one from its query string, so paging and counting behave the same.</summary>
    static SearchyRequest Searchy(IEnumerable<string> filters, int page, int pageSize, bool count) =>
        new(XchangeFilter.QueryString(filters,
            ("page", Math.Max(page, 0).ToString()),
            // Never 0: a size of 0 reads every matching row.
            ("size", Math.Clamp(pageSize, 1, MaxPageSize).ToString()),
            ("count", count ? "true" : "false")));

    static XchangeBulkRetry BulkRetry(string? status, DateTimeOffset? from, DateTimeOffset? to, int[]? partnerIds,
        int[]? subscriptionIds, int[]? informationTypeIds, string[]? ids, string? errorContains, string reason, bool reset)
    {
        var filters = XchangeFilter.Build(Status(status), from, to, partnerIds, subscriptionIds, informationTypeIds, ids,
            errorContains: errorContains);
        // No filter would mean every exchange there is.
        if (filters.Count == 0)
            throw new McpException("Give at least one filter: without one this would retry every exchange.");
        return new XchangeBulkRetry { Filter = XchangeFilter.QueryString(filters), Reason = reason, Reset = reset };
    }

    static async Task<XchangeRow> FindExchange(Xchanges.Search search, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new McpException("id is required.");
        id = id.Trim();
        // An id search also finds the exchange's retries, so the one asked for is picked out.
        for (var page = 0; page < 5; page++)
        {
            var rows = (SearchyResponse<XchangeRow>)await search.Handle(
                Searchy(XchangeFilter.Build(ids: [id]), page, MaxPageSize, count: false));
            if (rows.Result.FirstOrDefault(r => r.Id == id) is { } row) return WithoutFileLinks(row);
            if (rows.Result.Count() < MaxPageSize) break;
        }
        throw new McpException($"Bitween has no exchange {id}.");
    }

    static XchangeRow WithoutFileLinks(XchangeRow row)
    {
        row.InputUrl = null;
        row.OutputUrl = null;
        row.ResponseUrl = null;
        return row;
    }

    static Task<object> SetPaused(Subscriptions.Pause pause, int id, bool paused) =>
        pause.Handle(id, new SubscriptionPause { Paused = paused });

    /// <summary>One part of a combined answer: its result, or why it couldn't be read, so the rest still answers.</summary>
    async Task<object> Part(Func<Task<object>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception e) when (e is SWException or McpException)
        {
            return new { Error = Message(e) };
        }
    }

    /// <summary>
    /// The handler's answer as the API's JSON. Bitween's own refusals - permissions, validation, not
    /// found - become tool errors with their message; anything else the SDK reports without detail.
    /// </summary>
    async Task<string> Answer(Func<Task<object>> work)
    {
        try
        {
            return json.Write(await work());
        }
        catch (SWException e)
        {
            throw new McpException(Message(e), e);
        }
    }

    static string Message(Exception e) => e switch
    {
        SWUnauthorizedException => "Your Bitween role doesn't allow this. Ask an administrator for a role that does.",
        SWValidationException v when v.Validations?.Any() == true => string.Join(" ", v.Validations.Select(x => x.Value)),
        _ => e.Message,
    };
}
