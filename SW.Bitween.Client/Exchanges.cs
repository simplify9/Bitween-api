using SW.Bitween.Model;

namespace SW.Bitween.Client;

/// <summary>A Bitween's exchanges: what the Exchanges page shows and does.</summary>
public sealed class Exchanges
{
    readonly BitweenClient bitween;

    internal Exchanges(BitweenClient bitween) => this.bitween = bitween;

    /// <summary>
    /// A page of the exchanges <paramref name="query"/> matches, newest first, and how many match:
    /// <paramref name="page"/> counts from 0, and has <paramref name="size"/> exchanges. With
    /// <paramref name="count"/> false the total is left out, as 0, and the page comes back sooner.
    /// </summary>
    public Task<Page<XchangeRow>> SearchAsync(ExchangeQuery? query = null, int page = 0, int size = 25, bool count = true,
        CancellationToken cancellation = default)
    {
        var filters = (query ?? new ExchangeQuery()).ToFilters();
        var paging = new List<(string, string)> { ("page", page.ToString()), ("size", size.ToString()) };
        if (!count) paging.Add(("count", "false"));
        return bitween.GetAsync<Page<XchangeRow>>($"api/xchanges?{ExchangeQuery.QueryString(filters, paging.ToArray())}", cancellation);
    }

    /// <summary>One exchange, or null when there is none by that id.</summary>
    public async Task<XchangeRow?> GetAsync(string id, CancellationToken cancellation = default)
    {
        // The id search also finds the exchange's retries, which are newer, and the exchanges it rolled
        // up, which are older. One retry is all an exchange gets now, but chains from before that rule
        // can have several, so it is read a page at a time until the exchange is found or the rows end.
        var query = new ExchangeQuery { Ids = [id] };
        for (var page = 0; ; page++)
        {
            var found = await SearchAsync(query, page, 25, false, cancellation);
            var exchange = found.Result.FirstOrDefault(x => x.Id == id);
            if (exchange != null || found.Result.Count < 25) return exchange;
        }
    }

    /// <summary>
    /// One of an exchange's files, by the key its row gives: <see cref="XchangeRow.InputKey"/>,
    /// <see cref="XchangeRow.OutputKey"/> or <see cref="XchangeRow.ResponseKey"/>.
    /// </summary>
    public async Task<string> ReadFileAsync(string key, CancellationToken cancellation = default)
    {
        var file = await bitween.GetAsync<FileAnswer>($"api/bitweendocs?documentKey={Uri.EscapeDataString(key)}", cancellation);
        return file.Data;
    }

    /// <summary>Every attempt in the retry chain <paramref name="id"/> belongs to, oldest first.</summary>
    public Task<XchangeRetryTree> GetRetryTreeAsync(string id, CancellationToken cancellation = default) =>
        bitween.GetAsync<XchangeRetryTree>($"api/xchanges/retrytree?id={Uri.EscapeDataString(id)}", cancellation);

    /// <summary>
    /// Runs an exchange again from its input file, and returns the new attempt's id: null when it can't
    /// look that up, because the account may retry but not view exchanges or the lookup failed. With
    /// <paramref name="reset"/>, the subscription's current settings are used rather than those the
    /// exchange ran with. An exchange is retried at most once: one that has been is refused with
    /// <c>ALREADY_RETRIED</c>, so retry its newest attempt instead. One with an automatic retry booked
    /// is refused with <c>AUTO_RETRY_SCHEDULED</c>; run that with
    /// <see cref="ScheduledRetries.RunNowAsync"/> instead.
    /// </summary>
    public async Task<string?> RetryAsync(string id, bool reset = false, string reason = "Manual retry",
        CancellationToken cancellation = default)
    {
        await bitween.PostAsync($"api/xchanges/{Uri.EscapeDataString(id)}/retry",
            new XchangeRetry { Reason = reason, Reset = reset }, cancellation);
        // Answered with nothing: the new attempt is the newest that says it retries this. Looking it up
        // needs exchanges.view, and the retry is made by then, so a failed lookup mustn't read as the retry's.
        try
        {
            var retry = await SearchAsync(new ExchangeQuery { RetryFor = id }, 0, 1, false, cancellation);
            return retry.Result.FirstOrDefault()?.Id;
        }
        catch (Exception e) when (e is BitweenClientException or HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// What <see cref="BulkRetryAsync"/> would do with the same selection, changing nothing: how many
    /// will run, the attempts standing in for those already retried, and those skipped with why.
    /// </summary>
    public Task<XchangeBulkRetryPlan> PreviewBulkRetryAsync(ExchangeSelection selection, bool reset = false,
        CancellationToken cancellation = default) =>
        bitween.PostAsync<XchangeBulkRetryPlan>("api/xchanges/bulkretrypreview", BulkRetry(selection, "Bulk retry", reset), cancellation);

    /// <summary>
    /// Retries a selection, each chain once from its newest attempt, and says what it did. It carries
    /// out at most <see cref="XchangeBulkRetryPlan.Limit"/> (500) at a time: past that the plan comes
    /// back with <see cref="XchangeBulkRetryPlan.OverLimit"/> set and nothing done. To retry more, take
    /// <see cref="XchangeBulkRetryPlan.Targets"/> from the preview and retry them with
    /// <see cref="ExchangeSelection.Of"/>, that many at a time.
    /// </summary>
    public Task<XchangeBulkRetryPlan> BulkRetryAsync(ExchangeSelection selection, bool reset = false,
        string reason = "Bulk retry", CancellationToken cancellation = default) =>
        bitween.PostAsync<XchangeBulkRetryPlan>("api/xchanges/bulkretry", BulkRetry(selection, reason, reset), cancellation);

    /// <summary>
    /// How many exchanges <see cref="ExportAsync"/> would take, and how many of them are older than
    /// storage keeps files, so would come without them. Changes nothing.
    /// </summary>
    public Task<XchangeFilesExportCheck> CheckExportAsync(ExchangeSelection selection, CancellationToken cancellation = default) =>
        bitween.PostAsync<XchangeFilesExportCheck>("api/xchanges/export/check", Export(selection), cancellation);

    /// <summary>
    /// The selection's files as one zip, a folder for each exchange and <c>missing.txt</c> naming the
    /// files storage no longer has, read as it downloads. At most 500 exchanges; more is refused with
    /// <c>TOO_MANY</c>. Dispose the stream when done with it.
    /// </summary>
    public Task<Stream> ExportAsync(ExchangeSelection selection, CancellationToken cancellation = default) =>
        bitween.PostForStreamAsync("api/xchanges/export", Export(selection), cancellation);

    static XchangeBulkRetry BulkRetry(ExchangeSelection selection, string reason, bool reset) => new()
    {
        Ids = selection.Ids!,
        Filter = selection.Filter!,
        ExcludeIds = selection.ExcludeIds!,
        Reason = reason,
        Reset = reset,
    };

    static XchangeFilesExport Export(ExchangeSelection selection) => new()
    {
        Ids = selection.Ids,
        Filter = selection.Filter,
        ExcludeIds = selection.ExcludeIds,
    };

    sealed record FileAnswer(string Data, string Key);
}

/// <summary>A Bitween's automatic retries waiting to run: what the Scheduled retries page shows and does.</summary>
public sealed class ScheduledRetries
{
    readonly BitweenClient bitween;

    internal ScheduledRetries(BitweenClient bitween) => this.bitween = bitween;

    /// <summary>A page of the automatic retries waiting to run, soonest first, and how many there are.</summary>
    public Task<Page<DelayedRetryRow>> SearchAsync(ScheduledRetryQuery? query = null, int page = 0, int size = 25,
        CancellationToken cancellation = default)
    {
        var filters = (query ?? new ScheduledRetryQuery()).ToFilters();
        return bitween.GetAsync<Page<DelayedRetryRow>>(
            $"api/delayedretries?{ExchangeQuery.QueryString(filters, ("page", page.ToString()), ("size", size.ToString()))}", cancellation);
    }

    /// <summary>Runs the automatic retry booked for exchange <paramref name="id"/> now, rather than when it's due.</summary>
    public Task RunNowAsync(string id, CancellationToken cancellation = default) =>
        bitween.PostAsync($"api/delayedretries/{Uri.EscapeDataString(id)}/runnow", new DelayedRetryRunNow(), cancellation);
}
