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
/// it ended on prod. A rehearsal runs what came in on staging and compares the ending.
/// </summary>
/// <param name="ProdExchangeId">The exchange on prod, so the page can open it there.</param>
/// <param name="Outcome">success, badResponse or failed: how it ended on prod.</param>
/// <param name="Status">The partner's HTTP status, where prod's error names one.</param>
/// <param name="ErrorTypes">
/// The kinds of error prod's error names, outermost first: HttpRequestException, SocketException. Its
/// message isn't kept - it can hold prod's data - so the page opens the exchange on prod to read it.
/// </param>
public record MirrorSample(string ProdExchangeId, DateTime StartedOn, string Outcome, int? Status, IReadOnlyList<string> ErrorTypes,
    MirrorSampleFile Input, MirrorSampleFile Output, MirrorSampleFile Response);

/// <summary>A subscription's samples, or why it has none.</summary>
/// <param name="ProdId">The subscription on prod.</param>
public record MirrorSubscriptionSamples(string Subscription, string ProdId, IReadOnlyList<MirrorSample> Samples, string Reason);

/// <summary>The samples staging holds, and how the last taking went: what the page shows.</summary>
/// <param name="State">none (never taken), running, done or failed.</param>
/// <param name="Done">Subscriptions read so far, of <paramref name="Total"/>.</param>
/// <param name="Error">Why the last taking stopped, when it did.</param>
public record MirrorSamples(string State, DateTimeOffset? StartedOn, DateTimeOffset? FinishedOn, int Done, int Total, string Error,
    IReadOnlyList<MirrorSubscriptionSamples> Subscriptions);

/// <summary>How far a taking under way is.</summary>
public record MirrorSamplingProgress(DateTimeOffset StartedOn, int Done, int Total);

/// <summary>
/// Takes prod's samples in the background, one taking at a time, so the page can follow it: on a big
/// prod it takes longer than a request may wait. How it is going is known to the node doing it, and a
/// staging runs one node.
/// </summary>
public sealed class MirrorSampler(IServiceScopeFactory scopes, ILogger<MirrorSampler> logger)
{
    readonly Lock gate = new();
    Task running;

    /// <summary>The taking under way; null when none is.</summary>
    public MirrorSamplingProgress Running { get; private set; }

    /// <summary>Why the last taking stopped, until another starts; null when it finished.</summary>
    public string LastError { get; private set; }

    /// <summary>Starts a taking, unless one is under way.</summary>
    public void Start()
    {
        lock (gate)
        {
            if (running is { IsCompleted: false }) return;
            Running = new MirrorSamplingProgress(DateTimeOffset.UtcNow, 0, 0);
            LastError = null;
            running = Task.Run(RunAsync);
        }
    }

    async Task RunAsync()
    {
        var startedOn = Running.StartedOn;
        try
        {
            using var scope = scopes.CreateScope();
            var mirror = scope.ServiceProvider.GetRequiredService<MirrorService>();
            await mirror.TakeSamplesAsync((done, total) => Running = new MirrorSamplingProgress(startedOn, done, total));
        }
        catch (MirrorRefusal refusal)
        {
            LastError = refusal.Message;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Taking prod's samples stopped");
            LastError = $"Taking samples stopped: {ex.Message}";
        }
        finally
        {
            Running = null;
        }
    }
}
