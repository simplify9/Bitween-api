using System;
using System.Threading;
using System.Threading.Tasks;
using SW.PrimitiveTypes;
using SW.Serverless.Sdk.Resident;

namespace SW.Bitween.SampleResidentHandler;

/// <summary>
/// A handler that STAYS RUNNING, used exactly where a classic one would be - as a subscription's
/// handler or mapper.
///
/// It implements IInfolinkHandler like any other, so nothing about the pipeline's contract changes.
/// What differs is that the process is started once and kept, which is what a real one would use to
/// hold an open connection or a warm cache instead of paying for them on every message.
///
/// It counts what it has handled and reports that on the heartbeat, so a test can prove the SAME
/// process served more than one message - which is the whole distinction from the classic
/// lifecycle, and not something a single call could show.
/// </summary>
public class Handler : IResidentAdapter, IInfolinkHandler
{
    private IAdapterContext _context;
    private int _handled;
    private string _greeting = "resident";

    public Task StartAsync(IAdapterContext context, CancellationToken cancellationToken)
    {
        _context = context;
        _greeting = context.StartupValueOf("Greeting") ?? "resident";
        context.LogInformation($"Resident handler ready, greeting '{_greeting}'.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<AdapterStatus> GetStatusAsync()
    {
        var status = new AdapterStatus { Connected = true, State = "Ready" };
        status.Details["handled"] = _handled.ToString();
        return Task.FromResult(status);
    }

    /// <summary>
    /// The ordinary handler contract. The pipeline calls this by name and cannot tell which
    /// lifecycle answered it.
    /// </summary>
    public Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        var count = Interlocked.Increment(ref _handled);
        var body = xchangeFile?.Data ?? "";

        // The count is in the output on purpose: a classic adapter is a new process per message
        // and would answer 1 every time, so anything above 1 proves this instance was reused.
        var output = $"{{\"greeting\":\"{_greeting}\",\"handled\":{count},\"echo\":{Body(body)}}}";

        _context?.Metric("resident.handler.handled", 1);
        return Task.FromResult(new XchangeFile(output, xchangeFile?.Filename));
    }

    /// <summary>Keeps the echoed payload valid JSON whether or not the input was.</summary>
    private static string Body(string body) =>
        string.IsNullOrWhiteSpace(body) ? "null"
        : body.TrimStart().StartsWith('{') || body.TrimStart().StartsWith('[') ? body
        : "\"" + body.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
