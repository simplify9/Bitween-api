#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bitween.Services.Ai;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.XchangeExplanations;

public class XchangeExplanationRequest
{
    public string XchangeId { get; set; } = "";
}

public class XchangeExplanationResponse
{
    public string Summary { get; set; } = "";
    public string Cause { get; set; } = "";
    public string Fix { get; set; } = "";

    /// <summary>The model that wrote it.</summary>
    public string? Model { get; set; }

    /// <summary>Exactly what went to the provider, masked, so anyone can see what left Bitween.</summary>
    public string Sent { get; set; } = "";
}

/// <summary>
/// Explains a failed exchange in plain words, from its error, its setup and its documents - with
/// every value in them masked before anything is sent.
/// </summary>
/// <remarks>
/// <para>
/// What is sent, and nothing else: the error; which step failed; the subscription's kind and
/// adapters, and the names - never the values - of their settings; the retry policy's shape and the
/// retry state; the whole retry chain, each attempt's outcome and the first line of its error; and
/// the documents with their values replaced by types.
/// Adapter settings are names only because the exchange holds them with partner and global values
/// already filled in, and some of those are credentials.
/// </para>
/// <para>
/// A view right, like reading the exchange: an explanation changes nothing. Each one is recorded as
/// an operator action all the same, since it sends something about the exchange to a third party.
/// </para>
/// </remarks>
public class Explain(RequestContext requestContext, BitweenDbContext dbContext, XchangeService xchangeService,
        AiModels models)
    : ICommandHandler<XchangeExplanationRequest, XchangeExplanationResponse>
{
    /// <summary>The largest document masked and sent; a few records are enough to explain a failure.</summary>
    const int MaxMaskedFileBytes = 1_000_000;

    public async Task<XchangeExplanationResponse> Handle(XchangeExplanationRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Exchanges.View);
        var model = models.Current();

        var xchange = await dbContext.Set<Xchange>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.XchangeId)
                      ?? throw new SWNotFoundException(request.XchangeId);
        var result = await dbContext.Set<XchangeResult>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == xchange.Id);
        if (result is null)
            throw new SWValidationException("NOT_FINISHED", "This exchange hasn't finished yet, so there's nothing to explain.");
        if (result.Success && !result.ResponseBad)
            throw new SWValidationException("NOT_FAILED", "This exchange didn't fail.");

        var subscription = xchange.SubscriptionId is { } subscriptionId
            ? await dbContext.Subscriptions().AsNoTracking().FirstOrDefaultAsync(s => s.Id == subscriptionId)
            : null;
        var document = await dbContext.Set<Document>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == xchange.DocumentId);
        var delayed = await dbContext.Set<DelayedRetry>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == xchange.Id);

        var masking = new Masking();
        var sent = new StringBuilder();

        // Documents first: masking them is what tells the error masking which values to take out.
        var files = new List<(string Name, string Text)>();
        await AddFile(files, masking, xchange, XchangeFileType.Input, xchange.InputSize, xchange.InputContentType ?? ContentTypeOf(document));
        await AddFile(files, masking, xchange, XchangeFileType.Output, result.OutputSize, result.OutputContentType);
        await AddFile(files, masking, xchange, XchangeFileType.Response, result.ResponseSize, result.ResponseContentType);
        var chain = await Chain(xchange);
        var sendable = await SeeOtherAttempts(chain, xchange.Id, result.Exception, masking);

        sent.AppendLine("Failed step: " + FailedStep(xchange, result));
        if (subscription is not null)
        {
            sent.AppendLine($"Subscription kind: {subscription.Type}");
            sent.AppendLine($"Mapper: {subscription.MapperId ?? "none"}; handler: {subscription.HandlerId ?? "none"}");
        }
        else
        {
            sent.AppendLine($"Mapper: {xchange.MapperId ?? "none"}; handler: {xchange.HandlerId ?? "none"} (the subscription no longer exists)");
        }
        sent.AppendLine("Handler settings (names only): " + Names(xchange.HandlerProperties));
        sent.AppendLine("Document format: " + (document?.DocumentFormat.ToString() ?? "unknown"));
        if (result.ResponseBad) sent.AppendLine("The partner's response was marked as bad data.");
        sent.AppendLine($"This is attempt {chain.Position} of {chain.Total}{(chain.More ? " or more" : "")} in its retry chain.");
        if (!string.IsNullOrWhiteSpace(result.RetryBlockedReason))
            sent.AppendLine("Why it won't be retried: " + masking.Error(result.RetryBlockedReason));
        if (delayed is not null) sent.AppendLine($"A retry is scheduled for {delayed.On:u}.");
        sent.AppendLine(RetryPolicy(subscription));

        sent.AppendLine();
        sent.AppendLine("<error>");
        sent.AppendLine(string.IsNullOrWhiteSpace(result.Exception) ? "(no error text; see the response)" : masking.Error(result.Exception));
        sent.AppendLine("</error>");

        if (chain.Total > 1)
        {
            sent.AppendLine();
            sent.AppendLine("<retry_chain>");
            foreach (var line in ChainLines(chain, xchange.Id, masking, sendable)) sent.AppendLine(line);
            sent.AppendLine("</retry_chain>");
        }

        foreach (var (name, text) in files)
        {
            sent.AppendLine();
            sent.AppendLine($"<{name}_document>");
            sent.AppendLine(text);
            sent.AppendLine($"</{name}_document>");
        }

        var message = sent.ToString();

        // Recorded before it goes, so a call that fails or times out after sending is on record too.
        await dbContext.RecordNowAsync(requestContext, OperatorAction.ExplainFailure, xchange.Id,
            $"Sent to {models.ModelName}; documents and error masked.");
        var explanation = FailurePrompt.Parse(await model.Complete(new AiPrompt(FailurePrompt.Instructions, message, Json: true, MaxTokens: 4000)));

        return new XchangeExplanationResponse
        {
            Summary = explanation.Summary,
            Cause = explanation.Cause,
            Fix = explanation.Fix,
            Model = models.ModelName,
            Sent = message,
        };
    }

    async Task AddFile(List<(string, string)> files, Masking masking, Xchange xchange, XchangeFileType type, int size,
        string? contentType)
    {
        if (size <= 0) return;
        var name = type.ToString().ToLowerInvariant();
        // Masking reads the whole document before cutting what it sends; past this, it isn't read.
        if (size > MaxMaskedFileBytes)
        {
            files.Add((name, "(not sent: too large to mask)"));
            return;
        }
        string text;
        try
        {
            text = await xchangeService.GetFile(xchange, type);
        }
        catch (SWValidationException)
        {
            files.Add((name, "(no longer in storage)"));
            return;
        }
        files.Add((name, masking.Document(text, contentType)
                         ?? "(not sent: only JSON, XML and CSV documents can be masked)"));
    }

    /// <summary>Not stored, so worked out: no output from a mapping exchange means mapping failed.</summary>
    static string FailedStep(Xchange xchange, XchangeResult result) =>
        result.Success && result.ResponseBad ? "delivered, but the partner's response was bad"
        : xchange.MapperId is not null && result.OutputSize <= 0 ? "mapping"
        : "delivery (the handler)";

    static string Names(IReadOnlyDictionary<string, string>? properties) =>
        properties is null || properties.Count == 0
            ? "none"
            : string.Join(", ", properties.OrderBy(p => p.Key).Select(p => $"{p.Key} ({(string.IsNullOrEmpty(p.Value) ? "empty" : "set")})"));

    static string RetryPolicy(Subscription? subscription)
    {
        var groups = subscription?.RetryPolicy?.Groups ?? subscription?.CustomRetryPolicy?.Groups;
        if (groups is null || groups.Count == 0) return "Retry policy: none";
        return "Retry policy" + (subscription?.RetryPolicy?.Name is { } name ? $" \"{name}\"" : "") + ": " + string.Join("; ",
            groups.Select(g => $"{g.Name} ({(g.Enabled ? "on" : "off")}, applies to {string.Join("/", g.AppliesTo)}, {g.Action}" +
                               (g.Budget is { } b ? $", up to {b.MaxAttemptsTotal} attempts" : "") + ")"));
    }

    /// <summary>One attempt in a retry chain, numbered from the chain's first; no outcome while it is still running.</summary>
    internal sealed record Attempt(string Id, int Number, System.DateTime StartedOn, bool? Success, bool ResponseBad, string? Error);

    /// <summary>The attempts listed, where the explained one stands, and how many weren't listed.</summary>
    internal sealed record RetryChain(List<Attempt> Attempts, int Position, int Total, bool More, int EarlierNotListed);

    /// <summary>Attempts listed before and after the explained one; each side has its own room.</summary>
    const int ListedBefore = 30, ListedAfter = 20;

    /// <summary>How far the chain is followed each way to count it; past this it says "or more".</summary>
    const int Walked = 200;

    /// <summary>How much of each attempt's error is read: its first line, nearly always.</summary>
    internal const int ErrorRead = 4_000;

    /// <summary>The longest line an attempt's error is sent as, once masked.</summary>
    internal const int MaxLineLength = 500;

    /// <summary>Attempts with an error of their own whose documents are read to mask it.</summary>
    const int OtherAttemptsRead = 10;

    /// <summary>
    /// The retry chain this exchange is in: the attempts before it and after it, since an older
    /// attempt opened from a link still belongs to the chain's story.
    /// </summary>
    /// <remarks>
    /// Walked along RetryFor rather than read from the result's attempt number, which counts only
    /// the retries a policy made - five attempts by hand all say 1. An exchange is retried at most
    /// once, so the chain has no branches. Each side is counted in full up to <see cref="Walked"/>,
    /// and listed nearest first up to its own limit, so a long past never crowds out a later success.
    /// </remarks>
    async Task<RetryChain> Chain(Xchange xchange)
    {
        var before = new List<string>();
        var earlier = 0;
        var more = false;
        for (var id = xchange.RetryFor; id is not null;)
        {
            if (earlier == Walked) { more = true; break; }
            var previous = await dbContext.Set<Xchange>().AsNoTracking()
                .Where(x => x.Id == id).Select(x => new { x.Id, x.RetryFor }).FirstOrDefaultAsync();
            if (previous is null) break;
            earlier++;
            if (before.Count < ListedBefore) before.Add(previous.Id);
            id = previous.RetryFor;
        }
        var after = new List<string>();
        var later = 0;
        for (var id = xchange.Id;;)
        {
            if (later == Walked) { more = true; break; }
            var next = await dbContext.Set<Xchange>().AsNoTracking()
                .Where(x => x.RetryFor == id).OrderBy(x => x.StartedOn).Select(x => x.Id).FirstOrDefaultAsync();
            if (next is null) break;
            later++;
            if (after.Count < ListedAfter) after.Add(next);
            id = next;
        }

        before.Reverse();
        var ids = before.Append(xchange.Id).Concat(after).ToList();
        // One read for each attempt and its result, so one deleted meanwhile is gone, not "still running".
        var rows = await (
                from x in dbContext.Set<Xchange>().AsNoTracking()
                where ids.Contains(x.Id)
                join r in dbContext.Set<XchangeResult>().AsNoTracking() on x.Id equals r.Id into results
                from r in results.DefaultIfEmpty()
                select new
                {
                    x.Id, x.StartedOn,
                    Success = r == null ? (bool?)null : r.Success,
                    ResponseBad = r != null && r.ResponseBad,
                    Error = r == null || r.Exception == null ? null : r.Exception.Substring(0, ErrorRead),
                })
            .ToDictionaryAsync(r => r.Id);

        var first = earlier - before.Count + 1;
        var attempts = ids.Select((id, i) => (id, number: first + i))
            .Where(a => rows.ContainsKey(a.id))
            .Select(a =>
            {
                var r = rows[a.id];
                return new Attempt(a.id, a.number, r.StartedOn, r.Success, r.ResponseBad, r.Error);
            })
            .ToList();
        return new RetryChain(attempts, earlier + 1, earlier + 1 + later, more, earlier - before.Count);
    }

    /// <summary>
    /// Reads the documents of the other attempts whose error differs from this one's, so masking
    /// knows their values too: a later attempt's response can hold what this one's documents don't.
    /// Returns the attempts whose error line may be sent: this one, those with its very line, and
    /// those whose documents were all read and masked. Any other's line is withheld, not guessed at.
    /// </summary>
    async Task<HashSet<string>> SeeOtherAttempts(RetryChain chain, string thisId, string? thisError, Masking masking)
    {
        var thisLine = WholeFirstLine(thisError ?? "");
        var sendable = chain.Attempts
            .Where(a => a.Id == thisId || string.IsNullOrWhiteSpace(a.Error) || WholeFirstLine(a.Error) == thisLine)
            .Select(a => a.Id).ToHashSet();
        var ids = chain.Attempts.Where(a => !sendable.Contains(a.Id)).Select(a => a.Id).Take(OtherAttemptsRead).ToList();
        if (ids.Count == 0) return sendable;
        var xchanges = await dbContext.Set<Xchange>().AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync();
        var results = await dbContext.Set<XchangeResult>().AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id);
        foreach (var x in xchanges)
        {
            if (!results.TryGetValue(x.Id, out var r)) continue;
            if (await Seen(masking, x, XchangeFileType.Output, r.OutputSize, r.OutputContentType) &&
                await Seen(masking, x, XchangeFileType.Response, r.ResponseSize, r.ResponseContentType))
                sendable.Add(x.Id);
        }
        return sendable;
    }

    /// <summary>
    /// Masks one of another attempt's documents, for its values only: whether they are now known.
    /// Not when it is too large, gone, unreadable, or in a format masking can't read.
    /// </summary>
    async Task<bool> Seen(Masking masking, Xchange xchange, XchangeFileType type, int size, string? contentType)
    {
        if (size <= 0) return true;
        if (size > MaxMaskedFileBytes) return false;
        try
        {
            return masking.Document(await xchangeService.GetFile(xchange, type), contentType) != null;
        }
        catch (Exception)
        {
            // Storage that can't give it back now costs this attempt's line, not the explanation.
            return false;
        }
    }

    /// <summary>
    /// The chain as the model reads it: one line per attempt, its outcome and its error's first line,
    /// with a run of the same outcome and error folded into one line.
    /// </summary>
    internal static IEnumerable<string> ChainLines(RetryChain chain, string thisId, Masking masking, ISet<string> sendable)
    {
        string Describe(Attempt a)
        {
            if (a.Success is null) return "still running";
            if (a.Success == true && !a.ResponseBad) return "succeeded";
            var what = a.ResponseBad ? "delivered, bad response" : "failed";
            if (string.IsNullOrWhiteSpace(a.Error)) return what;
            if (!sendable.Contains(a.Id)) return $"{what}, with a different error, not sent: its documents couldn't be read to mask it";
            // Masked whole, then cut: a cut first could leave half a value masking can't recognise.
            var line = masking.Error(WholeFirstLine(a.Error));
            return $"{what}: {(line.Length > MaxLineLength ? line[..MaxLineLength] + " ..." : line)}";
        }

        if (chain.EarlierNotListed > 0)
            yield return $"({chain.EarlierNotListed} earlier attempts not listed)";
        var list = chain.Attempts;
        var i = 0;
        while (i < list.Count)
        {
            var what = Describe(list[i]);
            var j = i;
            // The opened attempt keeps a line of its own, so the model knows which one it is explaining.
            while (j + 1 < list.Count && list[j + 1].Id != thisId && list[i].Id != thisId && Describe(list[j + 1]) == what) j++;
            var at = list[i].StartedOn.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            var which = i == j ? $"Attempt {list[i].Number}" : $"Attempts {list[i].Number}-{list[j].Number}";
            var mark = list[i].Id == thisId ? " (this one)" : "";
            yield return $"{which}{mark}, from {at} UTC: {what}";
            i = j + 1;
        }
    }

    /// <summary>
    /// An error's first line. Only <see cref="ErrorRead"/> characters are read, so a first line that
    /// runs past them is cut back to before its last, possibly partial, word - and before a quote it
    /// opened and never closed - rather than send half a value masking can't recognise.
    /// </summary>
    internal static string WholeFirstLine(string text)
    {
        var newline = text.IndexOf('\n');
        if (newline >= 0) return text[..newline].Trim();
        if (text.Length < ErrorRead) return text.Trim();
        var end = Math.Max(text.LastIndexOf(' '), 0);
        foreach (var quote in new[] { '\'', '"' })
            if (text[..end].Count(c => c == quote) % 2 == 1)
                end = text.LastIndexOf(quote, end - 1);
        return (end > 0 ? text[..end].Trim() + " " : "") + "...";
    }

    static string? ContentTypeOf(Document? document) => document?.DocumentFormat switch
    {
        DocumentFormat.Json => "application/json",
        DocumentFormat.Xml => "application/xml",
        _ => null,
    };
}
