using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SW.Serverless.Resident;
using SW.Serverless.Sdk;

namespace SW.Bitween.Services.Adapters;

/// <summary>
/// Turns what a dying adapter printed into a sentence an operator can act on.
///
/// An adapter that fails during startup - a broker refusing the credentials, TLS attempted against
/// a plaintext port - writes the reason to its own stderr and exits. The host keeps that output,
/// but nothing carried it anywhere a person looks: the connection test said "Adapter stream
/// closed", the data source row's LastException stayed null because that field only ever held what
/// a LIVE adapter reported about itself, and the actual exception existed solely in the API log.
///
/// So the two questions an operator asks - "why did the test fail" and "why is this connection
/// down" - could only be answered by tailing a log on the node. This is what makes them
/// answerable from the screen that asked.
/// </summary>
public static class AdapterFailureReader
{
    /// <summary>Long enough for a real exception message, short enough to sit in a UI panel.</summary>
    private const int MaxLength = 2000;

    /// <summary>
    /// The most useful thing in the adapter's last output, or null when it said nothing.
    ///
    /// Prefers the last error it logged, because an adapter that dies logs the reason last. Falls
    /// back to the final line of anything at all - an adapter can also die without using the SDK's
    /// logger, and half an answer beats none.
    /// </summary>
    public static string Summarise(IEnumerable<string> diagnostics)
    {
        var lines = (diagnostics ?? []).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count == 0) return null;

        var error = lines.LastOrDefault(l => l.Contains(Constants.LogErrorIdentifier, StringComparison.Ordinal));

        return Clean(error ?? lines[^1]);
    }

    /// <summary>
    /// One error message with the adapter's last output appended, for a failure the caller already
    /// has an exception for. The exception says what Bitween observed ("Adapter stream closed");
    /// the output says why, which is the half worth reading.
    /// </summary>
    public static string Explain(string message, IEnumerable<string> diagnostics)
    {
        var detail = Summarise(diagnostics);

        if (string.IsNullOrWhiteSpace(detail)) return message;
        if (string.IsNullOrWhiteSpace(message)) return detail;

        // Not the other way round: the adapter's reason is what an operator acts on, so it must
        // survive being truncated in a narrow panel.
        return $"{detail} ({message})";
    }

    /// <summary>
    /// The adapter's output once it has finished arriving.
    ///
    /// Reading it the instant an invocation fails gets nothing: the failure is noticed when the
    /// gRPC stream ends, while the reason travelled by stderr and is still being pumped on another
    /// thread. Waiting for the process to exit is what makes the difference between "Adapter
    /// stream closed" and the exception that closed it.
    ///
    /// The wait is short and bounded, and an adapter that is still alive - a timeout rather than a
    /// crash - is not waited on at all.
    /// </summary>
    public static async Task<IEnumerable<string>> SettledOutputAsync(ResidentAdapterInstance instance,
        int graceMilliseconds = 2000)
    {
        if (instance == null) return null;

        try
        {
            var process = instance.Process;
            if (process is { HasExited: false })
            {
                using var timeout = new CancellationTokenSource(graceMilliseconds);
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { return instance.Diagnostics; }
            }

            // Exited, but the last stderr lines may not have been pumped yet. They arrive in
            // milliseconds; this waits for an error line rather than for the clock.
            for (var attempt = 0; attempt < 20; attempt++)
            {
                if (instance.Diagnostics.Any(l => l.IsError)) break;
                await Task.Delay(50);
            }

            return instance.Diagnostics;
        }
        catch
        {
            // Never let reading the reason become the reason.
            return instance.Diagnostics;
        }
    }

    /// <summary>
    /// The two questions this asks of a captured line, as extension members (C# 14) so the call
    /// sites read as properties of the line rather than as helpers taking one.
    /// </summary>
    extension(string line)
    {
        /// <summary>Whether the SDK marked this line as an error.</summary>
        private bool IsError =>
            line != null && line.Contains(Constants.LogErrorIdentifier, StringComparison.Ordinal);

        /// <summary>
        /// The line without the "12:34:56.789 " the host stamps on each captured line. Useful in a
        /// log tail, noise at the front of a sentence on a form.
        /// </summary>
        private string WithoutTimestamp
        {
            get
            {
                var space = line.IndexOf(' ');
                if (space != 8 && space != 12) return line;

                var head = line[..space];
                return head.Count(c => c == ':') == 2
                       && head.All(c => char.IsDigit(c) || c == ':' || c == '.')
                    ? line[(space + 1)..]
                    : line;
            }
        }
    }

    /// <summary>
    /// Undoes the SDK's line encoding. It escapes newlines so one log entry stays one line on the
    /// wire, which leaves a stack trace as a single unreadable string unless it is put back.
    /// </summary>
    private static string Clean(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var text = line
            .Replace(Constants.LogErrorIdentifier, "", StringComparison.Ordinal)
            .Replace(Constants.LogWarningIdentifier, "", StringComparison.Ordinal)
            .Replace(Constants.LogInformationIdentifier, "", StringComparison.Ordinal)
            .Replace(Constants.NewLineIdentifier, "\n", StringComparison.Ordinal)
            .Replace(Constants.Delimiter, "\n", StringComparison.Ordinal)
            .Trim();

        // The SDK writes "message#!#exception#!#", so what is left is the message, then the
        // exception's own first line - which is the type and text, and is the sentence to lead
        // with. Everything past it is a stack trace nobody reads on a data source screen.
        var parts = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;

        // The exception the adapter surfaced, then the innermost "--->" beneath it. That inner
        // line is where the diagnosis lives: "None of the specified endpoints were reachable" is
        // true of a wrong host, a wrong port and a firewall alike, while "Cannot determine the
        // frame size" says TLS was attempted against a plaintext port and nothing else.
        var lead = string.Join(" ", parts.Take(2).Select(p => p.WithoutTimestamp));

        var cause = parts
            .Where(l => l.StartsWith("--->", StringComparison.Ordinal))
            .Select(l => l[4..].Trim())
            .LastOrDefault(l => !string.IsNullOrWhiteSpace(l) && !lead.Contains(l, StringComparison.Ordinal));

        var summary = cause == null ? lead : $"{lead} - caused by {cause}";

        if (string.IsNullOrWhiteSpace(summary)) return null;

        return summary.Length > MaxLength ? summary[..MaxLength] + "…" : summary;
    }
}
