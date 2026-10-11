using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SW.PrimitiveTypes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Bitween.Adapters.Db;

/// <summary>
/// The receiver half: rows out of the database and into Bitween as Xchanges.
///
/// Modes are Kafka Connect's vocabulary, because it is the one operators already have -
/// <c>bulk</c>, <c>incrementing</c>, <c>timestamp</c>, <c>timestamp+incrementing</c> - plus
/// <c>marker</c>, a processed-flag column, which is what enterprise integration tables actually
/// tend to use. None of them can see a DELETE. That is a property of polling, not of this adapter,
/// and the UI says so rather than letting someone discover it.
///
/// Two things make this safe to run at all:
///
/// * The cursor is HOST-HELD, through <c>IAdapterContext.SetStateAsync</c>. An adapter cannot keep
///   its own progress - the supervisor restarts it and the next instance may be elsewhere - and a
///   cursor that resets to the beginning replays every row already processed.
/// * The cursor advances per row, in DeleteFile, which the pipeline calls only after Bitween has
///   durably accepted that row. Advancing on read instead would lose rows on a crash; advancing
///   only at the end of a batch would replay the batch. Per row is the one that is merely
///   at-least-once rather than either-way-wrong.
/// </summary>
public abstract partial class DbResidentAdapterBase
{
    /// <summary>
    /// One poll's rows, held between ListFiles and the GetFile / DeleteFile calls that follow.
    ///
    /// Keyed rather than kept in a field because this instance is SHARED: two subscriptions bound
    /// to the same data source can poll concurrently, and a field would have one overwrite the
    /// other's batch halfway through. The key travels inside each row id.
    /// </summary>
    readonly ConcurrentDictionary<string, ReceiveBatch> batches = new();

    sealed class ReceiveBatch
    {
        public Dictionary<string, Dictionary<string, object>> Rows { get; } = new();
        public Dictionary<string, object> Cursors { get; } = new();
        public DateTimeOffset StartedOn { get; } = DateTimeOffset.UtcNow;
        public int Outstanding { get; set; }
    }

    /// <summary>
    /// Where the cursor was kept before it was scoped per subscription. Read as a fallback so an
    /// upgrade does not reset progress to the beginning and replay every row already processed;
    /// never written, so the first advance after an upgrade moves to the scoped name and the old
    /// one is simply left behind.
    /// </summary>
    const string LegacyCursorStateName = "receive.cursor";

    /// <summary>
    /// The host holds state per (adapter, instance, name), and the instance here is the DATA
    /// SOURCE - one connection shared by every subscription pointed at it. So the name has to
    /// carry the reader, or two subscriptions polling one connection share a cursor: whichever
    /// polls first advances it, and the rows it took are invisible to the other. No error, no
    /// warning, half the rows each.
    ///
    /// The subscription id arrives as a per-invocation value, which is why this is computed per
    /// call rather than held in a field - the field would belong to whichever subscription
    /// happened to call first.
    /// </summary>
    string CursorStateName()
    {
        var subscriptionId = Context.ValueOf(SubscriptionIdKey);
        return string.IsNullOrWhiteSpace(subscriptionId)
            ? LegacyCursorStateName
            : $"{LegacyCursorStateName}.{subscriptionId}";
    }

    /// <summary>
    /// Set by the host on every invocation. Its Bitween-side name is
    /// <c>StartupValuesFiller.SubscriptionIdKey</c>; the two are literals on either side of a
    /// process boundary rather than a shared constant, because the adapter is published on its
    /// own and shares no assembly with the host.
    /// </summary>
    const string SubscriptionIdKey = "__subscriptionId__";

    /// <summary>
    /// Which subscription inherited the unscoped cursor. Inheriting it is a one-time migration for
    /// the receiver that was already running, not a starting point for every reader added later:
    /// without this, a second subscription pointed at the same connection would begin life at
    /// wherever the first one had got to, silently skipping every row before that.
    ///
    /// A marker rather than a delete because the SDK's state API has no delete.
    /// </summary>
    const string CursorClaimStateName = "receive.cursor.inheritedBy";

    /// <summary>
    /// The saved cursor: this subscription's own, or - once, for whichever subscription asks
    /// first - the unscoped one left behind by a version that did not scope them.
    /// </summary>
    async Task<string> ReadCursorAsync()
    {
        var name = CursorStateName();
        var saved = await Context.GetStateAsync(name, Stopping);

        // Already has its own progress, or there is no subscription id to scope by - either way
        // there is nothing to inherit.
        if (!string.IsNullOrEmpty(saved) || name == LegacyCursorStateName) return saved;

        var legacy = await Context.GetStateAsync(LegacyCursorStateName, Stopping);
        if (string.IsNullOrEmpty(legacy)) return null;

        var subscriptionId = Context.ValueOf(SubscriptionIdKey);
        var claimedBy = await Context.GetStateAsync(CursorClaimStateName, Stopping);

        if (string.IsNullOrEmpty(claimedBy))
        {
            await Context.SetStateAsync(CursorClaimStateName, subscriptionId, Stopping);
            Logger.LogInformation(
                "Subscription {Subscription} has no cursor of its own, so it continues from the "
                + "unscoped {Legacy} this connection used before cursors were scoped. No later "
                + "subscription will inherit it.", subscriptionId, LegacyCursorStateName);
            return legacy;
        }

        // Claimed by this one already, and it has not accepted a row yet - so the inherited value
        // is still where it is up to.
        if (claimedBy == subscriptionId) return legacy;

        // Claimed by someone else: this is a new reader, and a new reader starts at the beginning.
        // Anything else would hand it another subscription's progress as its own.
        return null;
    }

    /// <summary>
    /// What this poll is: which SQL, read how, with which columns meaning what.
    ///
    /// Resolved per invocation rather than read from <see cref="Options"/>, because one resident
    /// instance serves every subscription bound to the data source. The connection is shared; what
    /// to poll and how is not.
    ///
    /// Where each part comes from is the whole argument:
    ///
    /// * <b>The SQL</b> is a named statement, exactly like every other statement. It is a name and
    ///   never text, because a per-invocation property has <c>{{partner.X}}</c> substituted into it
    ///   before the adapter sees it - SQL there would be steerable by ordinary partner data.
    /// * <b>The cursor and key columns</b> come from that statement, because they describe what the
    ///   query returns. The same statement returns the same cursor column whoever reads it.
    /// * <b>The mode and batch size</b> come from the subscription, because they are the reader's
    ///   policy: the same statement is legitimately read in bulk once for a backfill and
    ///   incrementally thereafter, and batch size is one subscription's appetite.
    ///
    /// Every part falls back to the data source setting of the same name, so a receiver configured
    /// before any of this moved keeps working untouched.
    /// </summary>
    sealed class ReceivePlan
    {
        public string Mode { get; set; }
        public string Sql { get; set; }
        public string CursorColumn { get; set; }
        public string KeyColumn { get; set; }
        public string MarkProcessedSql { get; set; }
        public int BatchSize { get; set; }

        public bool NeedsCursor =>
            Mode is "incrementing" or "timestamp" or "timestamp+incrementing";
    }

    ReceivePlan Plan()
    {
        // Deliberately NOT Context.ValueOf, which falls back to the startup values. Startup values
        // are the DATA SOURCE's settings, and for two of these the two sources mean different
        // things: an invocation's ReceiveStatement is a statement NAME, while the data source's
        // legacy setting of that name is raw SQL. Reading through a fallback would take the second
        // and try to look it up as the first.
        //
        // So the invocation is read on its own, and Options is the explicit fallback below.
        var mine = Context.InvocationValues ?? new Dictionary<string, string>();
        string Given(string name) => mine.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

        var plan = new ReceivePlan
        {
            Mode = (Given("ReceiveMode") ?? Options.ReceiveMode ?? "").Trim().ToLowerInvariant(),
            CursorColumn = Options.CursorColumn,
            KeyColumn = Options.KeyColumn,
            Sql = Options.ReceiveStatement,
            MarkProcessedSql = Options.MarkProcessedStatement,
            BatchSize = Options.ReceiveBatchSize,
        };

        if (int.TryParse(Given("ReceiveBatchSize"), out var parsed) && parsed > 0)
            plan.BatchSize = parsed;

        // A named statement supersedes the data source's own receive settings entirely - SQL and
        // row shape together, because taking the SQL from one place and the cursor column from
        // another is how they come to disagree.
        var name = Given("ReceiveStatement");
        if (!string.IsNullOrWhiteSpace(name))
        {
            var statement = statements.Find(name)
                ?? throw new InvalidOperationException(
                    $"'{name}' is not a statement this data source defines, so there is nothing to "
                    + "poll with. Configured: "
                    + (statements.Count == 0
                        ? "none."
                        : string.Join(", ", statements.Names.OrderBy(n => n))));

            plan.Sql = statement.Sql;
            if (!string.IsNullOrWhiteSpace(statement.CursorColumn))
                plan.CursorColumn = statement.CursorColumn;
            if (!string.IsNullOrWhiteSpace(statement.KeyColumn))
                plan.KeyColumn = statement.KeyColumn;
        }

        var markName = Given("MarkProcessedStatement");
        if (!string.IsNullOrWhiteSpace(markName))
        {
            var statement = statements.Find(markName)
                ?? throw new InvalidOperationException(
                    $"'{markName}' is not a statement this data source defines, so accepted rows "
                    + "cannot be marked processed. Configured: "
                    + (statements.Count == 0
                        ? "none."
                        : string.Join(", ", statements.Names.OrderBy(n => n))));

            plan.MarkProcessedSql = statement.Sql;
        }

        return plan;
    }

    /// <summary>
    /// Nothing to do. The transaction a mark-processed statement might want cannot live here: the
    /// pipeline calls Initialize, then ListFiles, then a GetFile and DeleteFile per row, then
    /// Finalize - and holding one transaction open across all of that would pin a pooled connection
    /// for as long as Bitween takes to persist every row, on a shared instance serving other
    /// subscriptions at the same time. Each mark-processed runs in its own transaction instead,
    /// after Bitween has accepted the row, which is the at-least-once boundary anyway.
    /// </summary>
    public virtual Task Initialize()
    {
        PruneStaleBatches();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Polls for rows and returns one opaque id per row. The payload is read here, in one query,
    /// rather than re-selected per row: a round trip per row against a remote database turns a
    /// 500-row poll into 500 round trips.
    /// </summary>
    public virtual async Task<IEnumerable<string>> ListFiles()
    {
        var plan = Plan();

        if (string.IsNullOrEmpty(plan.Mode))
            throw new InvalidOperationException(
                "No ReceiveMode, so this cannot be used as a receiver. Set one on the subscription "
                + "(or on the data source, as a default) - one of bulk, incrementing, timestamp, "
                + "timestamp+incrementing or marker.");

        if (string.IsNullOrWhiteSpace(plan.Sql))
            throw new InvalidOperationException(
                "ReceiveMode is set but no statement was named, so there is nothing to poll with. "
                + "Name one of this data source's statements as ReceiveStatement.");

        if (string.IsNullOrWhiteSpace(plan.KeyColumn))
            throw new InvalidOperationException(
                "KeyColumn is required for receiving: without it a row cannot be identified, so it "
                + "cannot be marked processed and it cannot be deduplicated. Set it on the "
                + "statement being polled.");

        if (plan.NeedsCursor && string.IsNullOrWhiteSpace(plan.CursorColumn))
            throw new InvalidOperationException(
                $"ReceiveMode '{plan.Mode}' follows a column, so the statement being polled has to "
                + "say which column is its cursor.");

        if (plan.Mode is "bulk" or "marker" && string.IsNullOrWhiteSpace(plan.MarkProcessedSql))
            throw new InvalidOperationException(
                $"ReceiveMode '{plan.Mode}' has no cursor, so MarkProcessedStatement is what stops "
                + "the same rows being read again on every poll. Name one, or use a cursor mode.");

        var parameters = new Dictionary<string, object>();
        if (plan.NeedsCursor)
        {
            var saved = await ReadCursorAsync();
            parameters["cursor"] = ParseCursor(saved, plan.Mode);

            Logger.LogDebug("Polling from cursor {Cursor} ({Mode}).", saved ?? "(none)", plan.Mode);
        }

        var page = await QueryCore(new StatementRequest
        {
            Sql = plan.Sql,
            Parameters = parameters,
            MaxRows = plan.BatchSize,

            // Already resolved from the allow-list by Plan(), so this is configured SQL rather
            // than SQL a message supplied - it does not go through the ad-hoc gate again.
            Name = null
        }, adHocAllowed: true);

        // A page that filled exactly to the batch size leaves a cursor open behind it; the next
        // poll picks up from the saved cursor, so it is closed rather than carried.
        if (page.CursorId != null) await CloseCursor(page.CursorId);

        if (page.Rows.Count == 0) return Array.Empty<string>();

        var batchId = Guid.NewGuid().ToString("N");
        var batch = new ReceiveBatch { Outstanding = page.Rows.Count };
        var ids = new List<string>(page.Rows.Count);

        foreach (var row in page.Rows)
        {
            if (!row.TryGetValue(plan.KeyColumn, out var key) || key == null)
                throw new InvalidOperationException(
                    $"A row came back with no value in the key column '{plan.KeyColumn}'. The "
                    + "receive statement has to select it, spelled as the database returns it.");

            var id = $"{batchId}:{key}";
            batch.Rows[id] = row;

            if (plan.NeedsCursor && row.TryGetValue(plan.CursorColumn, out var cursorValue))
                batch.Cursors[id] = cursorValue;

            ids.Add(id);
        }

        batches[batchId] = batch;
        Logger.LogInformation("Polled {Count} row(s) in batch {Batch}.", ids.Count, batchId);
        return ids;
    }

    /// <summary>The row, as JSON, exactly as the query returned it.</summary>
    public virtual Task<XchangeFile> GetFile(string fileId)
    {
        var row = Locate(fileId, out _);
        var json = JsonConvert.SerializeObject(row);

        // Named after the row, so an operator looking at an Xchange can see which one it was.
        return Task.FromResult(new XchangeFile(json, $"{KeyOf(fileId)}.json"));
    }

    /// <summary>
    /// Called once Bitween has durably accepted the row, and therefore the point at which progress
    /// becomes real: the mark-processed statement runs, and the cursor advances past this row.
    ///
    /// Both, in that order. Advancing the cursor first would skip a row whose marking failed.
    /// </summary>
    public virtual async Task DeleteFile(string fileId)
    {
        var row = Locate(fileId, out var batch);
        var key = KeyOf(fileId);
        var plan = Plan();

        if (!string.IsNullOrWhiteSpace(plan.MarkProcessedSql))
        {
            var parameters = new Dictionary<string, object> { ["key"] = RawKey(row, plan) };

            // Every column is offered as a parameter too, so a mark statement can use more than the
            // key - a status column, a batch id, the row's own timestamp.
            foreach (var kv in row) parameters[kv.Key] = kv.Value;

            await Execute(new StatementRequest
            {
                Sql = plan.MarkProcessedSql,
                Parameters = parameters
            }, adHocAllowed: true);
        }

        if (batch.Cursors.TryGetValue(fileId, out var cursorValue) && cursorValue != null)
            await Context.SetStateAsync(CursorStateName(), FormatCursor(cursorValue), Stopping);

        batch.Rows.Remove(fileId);
        batch.Outstanding--;

        Logger.LogDebug("Row {Key} processed; {Outstanding} left in its batch.", key, batch.Outstanding);
    }

    /// <summary>
    /// Drops whatever the run did not get through. Rows left here were never accepted by Bitween
    /// and the cursor never moved past them, so the next poll reads them again - which is the
    /// correct at-least-once behaviour, not a leak.
    /// </summary>
    public virtual Task Finalize()
    {
        foreach (var kv in batches.ToArray())
            if (kv.Value.Outstanding <= 0 || kv.Value.Rows.Count == 0)
                batches.TryRemove(kv.Key, out _);

        PruneStaleBatches();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ helpers

    Dictionary<string, object> Locate(string fileId, out ReceiveBatch batch)
    {
        var separator = fileId?.IndexOf(':') ?? -1;
        if (separator <= 0)
            throw new ArgumentException($"'{fileId}' is not a row id this receiver handed out.", nameof(fileId));

        var batchId = fileId.Substring(0, separator);
        if (!batches.TryGetValue(batchId, out batch))
            throw new InvalidOperationException(
                $"Batch {batchId} is no longer held. Either the run already finished, or the adapter "
                + "restarted between the listing and this call - in which case the rows were never "
                + "acknowledged and the next poll will read them again.");

        if (!batch.Rows.TryGetValue(fileId, out var row))
            throw new InvalidOperationException($"Row '{fileId}' has already been processed in this run.");

        return row;
    }

    static string KeyOf(string fileId) => fileId.Substring(fileId.IndexOf(':') + 1);

    /// <summary>
    /// The row's key as the database returned it - not the string from the file id, which has been
    /// through JSON and would bind a number as text.
    /// </summary>
    static object RawKey(Dictionary<string, object> row, ReceivePlan plan) =>
        row.TryGetValue(plan.KeyColumn, out var value) ? value : null;

    /// <summary>
    /// A cursor comes back from the host as text, and has to go into the query as the type the
    /// column compares against - a string on one side of a timestamp comparison silently matches
    /// nothing on some engines rather than failing.
    /// </summary>
    object ParseCursor(string saved, string mode)
    {
        if (string.IsNullOrEmpty(saved))
            // The floor for a first run. Not null: null in a WHERE comparison excludes every row,
            // so a first poll would find nothing and never start.
            return mode.StartsWith("timestamp", StringComparison.Ordinal)
                ? (object)new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                : 0L;

        if (mode.StartsWith("timestamp", StringComparison.Ordinal))
            return DateTime.TryParse(saved, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"The saved cursor '{saved}' is not a timestamp. Clear the adapter state for "
                    + "this data source, or switch the mode to match the column.");

        return long.TryParse(saved, out var number) ? number : (object)saved;
    }

    static string FormatCursor(object value) => value switch
    {
        DateTime dt => dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value?.ToString()
    };

    /// <summary>
    /// A batch whose run died mid-way - the job was cancelled, the node went down - is otherwise
    /// held for the life of the process, and it holds every row of that poll in memory.
    /// </summary>
    void PruneStaleBatches()
    {
        var deadline = DateTimeOffset.UtcNow.AddHours(-1);
        foreach (var kv in batches.ToArray())
            if (kv.Value.StartedOn < deadline && batches.TryRemove(kv.Key, out _))
                Logger.LogWarning(
                    "Dropped receive batch {Batch}: it was started over an hour ago and never "
                    + "finished. Its {Count} unacknowledged row(s) will be read again.",
                    kv.Key, kv.Value.Rows.Count);
    }
}
