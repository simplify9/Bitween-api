using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SW.Bitween.Services.Mirror;

/// <summary>One of prod's exchange files as staging keeps it.</summary>
/// <param name="Key">Where it is in staging's storage; null when it isn't kept.</param>
/// <param name="Name">Its name, faked as any text is.</param>
/// <param name="Format">json, xml, html, csv, xlsx or text when it is kept; what it is when it isn't.</param>
/// <param name="NotCopied">Why it isn't kept, in words for the page; null when it is.</param>
/// <param name="Url">A link that opens it on staging, made when the page asks; never stored.</param>
public record MirrorSampleFile(string Key, string Name, string Format, int Size, string NotCopied, string Url = null);

/// <summary>
/// One of prod's exchanges, faked: what came in, what Bitween sent, what the partner answered, and how
/// it ended on prod. A rehearsal runs it on staging, as part of its family, and compares the ending.
/// </summary>
/// <param name="ProdExchangeId">The exchange on prod, so the page can open it there.</param>
/// <param name="Subscription">Its subscription, by name; null for a document being routed.</param>
/// <param name="InformationType">The type of what came in, by name.</param>
/// <param name="Parent">The exchange on prod whose answer, or whose message, started it; null for the one that came in.</param>
/// <param name="Outcome">success, badResponse or failed: how it ended on prod.</param>
/// <param name="Status">The partner's HTTP status, where prod's error names one.</param>
/// <param name="ErrorTypes">
/// The kinds of error prod's error names, outermost first: HttpRequestException, SocketException. Its
/// message isn't kept - it can hold prod's data - so the page opens the exchange on prod to read it.
/// </param>
/// <param name="Partner">The partner it ran as, by name: a partner calling an API gateway, or the subscription's own.</param>
/// <param name="Handler">The handler it ran, as prod recorded it: the adapter, and its version where one was pinned.</param>
/// <param name="Mapper">The mapper it ran, the same way.</param>
/// <param name="Rolled">For an aggregation: a few of the files it rolled up, faked, for staging to roll up the like.</param>
public record MirrorSample(string ProdExchangeId, DateTime StartedOn, string Outcome, int? Status, IReadOnlyList<string> ErrorTypes,
    MirrorSampleFile Input, MirrorSampleFile Output, MirrorSampleFile Response,
    string Partner = null, string Handler = null, string Mapper = null,
    string Subscription = null, string InformationType = null, string Parent = null,
    IReadOnlyList<MirrorSampleFile> Rolled = null);

/// <summary>
/// One thing that came in on prod and every exchange it led to: a document routed to the subscriptions
/// that wanted it, a partner's call through a gateway, a file a receiver read, an aggregation - then the
/// answers those handed on, to response subscriptions or as messages to bus gateways. A rehearsal sends
/// the first in the way it came in, and staging runs the rest by itself.
/// </summary>
/// <param name="Id">The first exchange's id on prod.</param>
/// <param name="Entry">How it came in: document, gateway, api (the partner API), receiver, aggregation or direct.</param>
/// <param name="Door">What it came in through, by name: the information type, the gateway, the receiving subscription.</param>
/// <param name="Partner">The partner who called, for a gateway or the partner API.</param>
/// <param name="Members">Its exchanges, in the order they started on prod; the first is the one that came in.</param>
/// <param name="Cut">How many more exchanges prod's family had than the sample keeps.</param>
/// <param name="Unlinked">
/// Prod doesn't say which subscriptions a document of it went to - it came in without a correlation
/// id - so the subscriptions staging routes it to are run, but not counted as more than prod's.
/// </param>
public record MirrorFamily(string Id, string Entry, string Door, string Partner, IReadOnlyList<MirrorSample> Members, int Cut = 0,
    bool Unlinked = false);

/// <summary>A subscription's place in the samples: the families it takes part in, or why there are none.</summary>
/// <param name="ProdId">The subscription on prod.</param>
public record MirrorSubscriptionSamples(string Subscription, string ProdId, IReadOnlyList<string> Families, string Reason);

/// <summary>The samples staging holds, and how the last taking went: what the page shows.</summary>
/// <param name="State">none (never taken), running, done or failed.</param>
/// <param name="Done">Subscriptions read so far, of <paramref name="Total"/>.</param>
/// <param name="Error">Why the last taking stopped, when it did.</param>
public record MirrorSamples(string State, DateTimeOffset? StartedOn, DateTimeOffset? FinishedOn, int Done, int Total, string Error,
    IReadOnlyList<MirrorSubscriptionSamples> Subscriptions, IReadOnlyList<MirrorFamily> Families = null);

/// <summary>How far the mirror's work under way is.</summary>
/// <param name="Kind">samples or rehearsal.</param>
/// <param name="Done">Done so far, of <paramref name="Total"/>: subscriptions read, or samples run.</param>
/// <param name="Subject">The subscription whose test it is, by name; null for work on every subscription.</param>
public record MirrorProgress(string Kind, DateTimeOffset StartedOn, int Done, int Total, string Subject = null);

/// <summary>
/// Runs the mirror's long work in the background - taking prod's samples, or rehearsing them - one at a
/// time, so the page can follow it: on a big prod it takes longer than a request may wait, and a
/// rehearsal running while its samples are replaced would compare the wrong files. How it is going is
/// known to the node doing it, and a staging runs one node.
/// </summary>
public sealed class MirrorWork(IServiceScopeFactory scopes, ILogger<MirrorWork> logger)
{
    public const string Sampling = "samples", Rehearsing = "rehearsal";

    readonly Lock gate = new();
    readonly Dictionary<string, string> lastErrors = new(StringComparer.Ordinal);
    Task running;

    /// <summary>The work under way; null when none is.</summary>
    public MirrorProgress Running { get; private set; }

    /// <summary>Why the last work of a kind stopped, for a subscription's test or for all, until it starts again; null when it finished.</summary>
    public string LastError(string kind, string subject = null)
    {
        lock (gate) return lastErrors.GetValueOrDefault(ErrorKey(kind, subject));
    }

    /// <summary>What work under way is, in words for the page: a subscription's test, taking samples, or a rehearsal.</summary>
    public static string Describe(MirrorProgress running) =>
        running.Subject != null ? $"The test of “{running.Subject}”"
        : running.Kind == Sampling ? "Taking samples of prod's exchanges"
        : "A rehearsal";

    static string ErrorKey(string kind, string subject) => subject == null ? kind : $"{kind}\u0000{subject}";

    /// <summary>
    /// Starts work of a kind, for one subscription's test or for all, unless work is under way: then says
    /// what is - which kind, and for which subscription - and starts nothing.
    /// </summary>
    public MirrorProgress Start(string kind, Func<MirrorService, Action<int, int>, CancellationToken, Task> work, string subject = null)
    {
        lock (gate)
        {
            if (running is { IsCompleted: false }) return Running;
            Running = new MirrorProgress(kind, DateTimeOffset.UtcNow, 0, 0, subject);
            lastErrors.Remove(ErrorKey(kind, subject));
            running = Task.Run(() => RunAsync(kind, subject, work));
            return null;
        }
    }

    async Task RunAsync(string kind, string subject, Func<MirrorService, Action<int, int>, CancellationToken, Task> work)
    {
        var startedOn = Running.StartedOn;
        string error = null;
        try
        {
            using var scope = scopes.CreateScope();
            var mirror = scope.ServiceProvider.GetRequiredService<MirrorService>();
            await work(mirror, (done, total) => Running = new MirrorProgress(kind, startedOn, done, total, subject), CancellationToken.None);
        }
        catch (MirrorRefusal refusal)
        {
            error = refusal.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The prod mirror's {Kind} stopped", kind);
            var what = kind == Sampling ? subject == null ? "Taking samples" : "Taking prod's arrivals" : subject == null ? "The rehearsal" : "The test";
            error = $"{what} stopped: {ex.Message}";
        }
        finally
        {
            lock (gate)
            {
                if (error != null) lastErrors[ErrorKey(kind, subject)] = error;
                Running = null;
            }
        }
    }
}
