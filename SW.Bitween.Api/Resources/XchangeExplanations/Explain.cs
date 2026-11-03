#nullable enable
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
        var chain = await Chain(xchange);
        var position = chain.FindIndex(a => a.Id == xchange.Id) + 1;
        sent.AppendLine($"This is attempt {position} of {chain.Count} in its retry chain" + (chain.Count >= MaxChain ? " (or more)" : "") + ".");
        if (!string.IsNullOrWhiteSpace(result.RetryBlockedReason))
            sent.AppendLine("Why it won't be retried: " + masking.Error(result.RetryBlockedReason));
        if (delayed is not null) sent.AppendLine($"A retry is scheduled for {delayed.On:u}.");
        sent.AppendLine(RetryPolicy(subscription));

        sent.AppendLine();
        sent.AppendLine("<error>");
        sent.AppendLine(string.IsNullOrWhiteSpace(result.Exception) ? "(no error text; see the response)" : masking.Error(result.Exception));
        sent.AppendLine("</error>");

        if (chain.Count > 1)
        {
            sent.AppendLine();
            sent.AppendLine("<retry_chain>");
            foreach (var line in ChainLines(chain, xchange.Id, masking)) sent.AppendLine(line);
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

    /// <summary>One attempt in a retry chain: null outcome fields while it is still running.</summary>
    sealed record Attempt(string Id, System.DateTime StartedOn, bool? Success, bool ResponseBad, string? Error);

    /// <summary>
    /// The whole retry chain this exchange is in, first attempt first: the ones before it and the
    /// ones after, since an older attempt opened from a link still belongs to the chain's story.
    /// </summary>
    /// <remarks>
    /// Walked along RetryFor rather than read from the result's attempt number, which counts only
    /// the retries a policy made - five attempts by hand all say 1. An exchange is retried at most
    /// once, so the chain has no branches.
    /// </remarks>
    async Task<List<Attempt>> Chain(Xchange xchange)
    {
        var ids = new List<string> { xchange.Id };
        for (var id = xchange.RetryFor; id is not null && ids.Count < MaxChain;)
        {
            var previous = await dbContext.Set<Xchange>().AsNoTracking()
                .Where(x => x.Id == id).Select(x => new { x.Id, x.RetryFor }).FirstOrDefaultAsync();
            if (previous is null) break;
            ids.Insert(0, previous.Id);
            id = previous.RetryFor;
        }
        for (var id = xchange.Id; ids.Count < MaxChain;)
        {
            var next = await dbContext.Set<Xchange>().AsNoTracking()
                .Where(x => x.RetryFor == id).OrderBy(x => x.StartedOn).Select(x => x.Id).FirstOrDefaultAsync();
            if (next is null) break;
            ids.Add(next);
            id = next;
        }

        // Only the start of each error: its first line is what is sent.
        var started = await dbContext.Set<Xchange>().AsNoTracking()
            .Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.StartedOn })
            .ToDictionaryAsync(x => x.Id, x => x.StartedOn);
        var results = await dbContext.Set<XchangeResult>().AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .Select(r => new { r.Id, r.Success, r.ResponseBad, Error = r.Exception == null ? null : r.Exception.Substring(0, 1000) })
            .ToDictionaryAsync(r => r.Id);
        return ids.Select(id => results.TryGetValue(id, out var r)
                ? new Attempt(id, started.GetValueOrDefault(id), r.Success, r.ResponseBad, r.Error)
                : new Attempt(id, started.GetValueOrDefault(id), null, false, null))
            .ToList();
    }

    /// <summary>
    /// The chain as the model reads it: one line per attempt, its outcome and its error's first line,
    /// with a run of the same outcome and error folded into one line.
    /// </summary>
    static IEnumerable<string> ChainLines(List<Attempt> chain, string thisId, Masking masking)
    {
        string Describe(Attempt a) =>
            a.Success is null ? "still running"
            : a.Success == true && !a.ResponseBad ? "succeeded"
            : (a.ResponseBad ? "delivered, bad response" : "failed") +
              (string.IsNullOrWhiteSpace(a.Error) ? "" : ": " + masking.Error(FirstLine(a.Error)));

        var i = 0;
        while (i < chain.Count)
        {
            var what = Describe(chain[i]);
            var j = i;
            // The opened attempt keeps a line of its own, so the model knows which one it is explaining.
            while (j + 1 < chain.Count && chain[j + 1].Id != thisId && chain[i].Id != thisId && Describe(chain[j + 1]) == what) j++;
            var at = chain[i].StartedOn.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            var which = i == j ? $"Attempt {i + 1}" : $"Attempts {i + 1}-{j + 1}";
            var mark = chain[i].Id == thisId ? " (this one)" : "";
            yield return $"{which}{mark}, from {at} UTC: {what}";
            i = j + 1;
        }
    }

    /// <summary>A chain is walked this far at most, both ways together.</summary>
    const int MaxChain = 50;

    static string FirstLine(string? text) => (text ?? "").Split('\n')[0].Trim();

    static string? ContentTypeOf(Document? document) => document?.DocumentFormat switch
    {
        DocumentFormat.Json => "application/json",
        DocumentFormat.Xml => "application/xml",
        _ => null,
    };
}
