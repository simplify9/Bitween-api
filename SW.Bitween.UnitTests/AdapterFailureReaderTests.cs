using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Services.Adapters;

namespace SW.Bitween.UnitTests;

/// <summary>
/// Reading an adapter's dying words.
///
/// These are the exact lines a resident adapter writes when it cannot reach a broker. Before this
/// existed, all of it stayed in the node's log: the connection test answered "Adapter stream
/// closed" and the data source row's reason column stayed empty, so the one screen built to save
/// an operator from reading a log was the screen that sent them to read one.
/// </summary>
[TestClass]
public class AdapterFailureReaderTests
{
    /// <summary>The real shape: SDK marker, message, delimiter, then the exception with its newlines escaped.</summary>
    private const string TlsOnPlainPort =
        "{{log.error}}Resident adapter terminated.#!#RabbitMQ.Client.Exceptions.BrokerUnreachableException: "
        + "None of the specified endpoints were reachable{{newline}} ---> "
        + "System.Security.Authentication.AuthenticationException: Cannot determine the frame size or a "
        + "corrupted frame was received.{{newline}}   at System.Net.Security.SslStream.GetFrameSize#!#";

    [TestMethod]
    public void The_reason_survives_the_wire_encoding()
    {
        var summary = AdapterFailureReader.Summarise([TlsOnPlainPort]);

        // What the operator needs is the exception's own first line. The SDK escapes newlines so
        // one log entry stays one line on the wire, which leaves this unreadable unless undone.
        StringAssert.Contains(summary, "BrokerUnreachableException");
        StringAssert.Contains(summary, "None of the specified endpoints were reachable");
        Assert.IsFalse(summary.Contains("{{newline}}"));
        Assert.IsFalse(summary.Contains("{{log.error}}"));
        Assert.IsFalse(summary.Contains("#!#"));
    }

    /// <summary>
    /// The line that actually diagnoses it. "None of the specified endpoints were reachable" is
    /// equally true of a wrong host, a wrong port and a firewall; "Cannot determine the frame
    /// size" says TLS was attempted against a plaintext port, and nothing else does.
    /// </summary>
    [TestMethod]
    public void The_innermost_cause_is_carried_too()
    {
        var summary = AdapterFailureReader.Summarise([TlsOnPlainPort]);

        StringAssert.Contains(summary, "caused by");
        StringAssert.Contains(summary, "Cannot determine the frame size");
    }

    /// <summary>The host stamps a time on each captured line; it is noise at the front of a sentence.</summary>
    [TestMethod]
    public void The_capture_timestamp_is_dropped()
    {
        var summary = AdapterFailureReader.Summarise(["10:27:28.223 " + TlsOnPlainPort]);

        StringAssert.StartsWith(summary, "Resident adapter terminated.");
    }

    [TestMethod]
    public void The_stack_trace_is_left_out()
    {
        var summary = AdapterFailureReader.Summarise([TlsOnPlainPort]);

        // A data source screen has room for the sentence, not the frames.
        Assert.IsFalse(summary.Contains("at System.Net.Security"));
    }

    [TestMethod]
    public void The_last_error_wins_over_ordinary_chatter()
    {
        string[] output =
        [
            "{{log.information}}Starting up",
            "{{log.error}}First problem#!#System.Exception: one#!#",
            "{{log.information}}Retrying",
            "{{log.error}}Resident adapter terminated.#!#System.Exception: the one that killed it#!#"
        ];

        StringAssert.Contains(AdapterFailureReader.Summarise(output), "the one that killed it");
    }

    [TestMethod]
    public void An_adapter_that_never_used_the_logger_still_says_something()
    {
        // Dying outside the SDK's logger is allowed - an unhandled exception on a background
        // thread prints a bare stack trace - and half an answer beats none.
        var summary = AdapterFailureReader.Summarise(["Unhandled exception. System.IO.IOException: pipe broken"]);

        StringAssert.Contains(summary, "IOException");
    }

    [TestMethod]
    public void Silence_stays_silence()
    {
        Assert.IsNull(AdapterFailureReader.Summarise(null));
        Assert.IsNull(AdapterFailureReader.Summarise([]));
        Assert.IsNull(AdapterFailureReader.Summarise(["   ", ""]));
    }

    /// <summary>
    /// The reason leads and Bitween's own observation follows in brackets. That order is the
    /// point: "Adapter stream closed" is what Bitween saw, and it is the half that explains
    /// nothing, so it must be the half that gets truncated in a narrow panel.
    /// </summary>
    [TestMethod]
    public void Explain_leads_with_the_adapters_reason()
    {
        var explained = AdapterFailureReader.Explain("Adapter stream closed.", [TlsOnPlainPort]);

        StringAssert.StartsWith(explained, "Resident adapter terminated.");
        StringAssert.Contains(explained, "(Adapter stream closed.)");
    }

    [TestMethod]
    public void Explain_falls_back_to_what_bitween_saw()
    {
        // No output at all - the process died before printing, or was killed outright.
        Assert.AreEqual("Adapter stream closed.",
            AdapterFailureReader.Explain("Adapter stream closed.", null));
    }
}
