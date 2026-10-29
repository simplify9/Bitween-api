using System;
using System.Diagnostics;
using System.Threading.Tasks;
using SW.Bitween.Services.Adapters;
using SW.Bitween.Services.Metrics;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// A built-in adapter's call that never returns: it used to hold its place in the processing queue for
/// good (an SFTP server that signed in and said nothing stopped all of Bitween's processing in the lab).
/// Now it fails as a timeout after Bitween:NativeAdapterTimeout, and what it holds open is closed.
/// </summary>
public class NativeAdapterTimeoutTests
{
    sealed class Held : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public async Task A_call_that_never_returns_fails_as_a_timeout_and_its_adapter_is_released()
    {
        var adapter = new Held();
        var never = new TaskCompletionSource<object>().Task;
        var clock = Stopwatch.StartNew();

        var failure = await Assert.ThrowsAsync<TimeoutException>(() =>
            NativeAdapterRuntime.Within(never, TimeSpan.FromMilliseconds(200), adapter, "nativesftpuploadhandler", "Handle"));

        Assert.InRange(clock.Elapsed, TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(10));
        Assert.Contains("nativesftpuploadhandler did not finish Handle within", failure.Message);
        Assert.True(adapter.Disposed);
        Assert.Equal(AdapterFailure.Timeout, AdapterInvoker.Classify(failure));
    }

    [Fact]
    public async Task A_call_that_finishes_in_time_or_has_no_limit_is_untouched()
    {
        var adapter = new Held();
        Assert.Equal("done", await NativeAdapterRuntime.Within(Task.FromResult<object>("done"), TimeSpan.FromSeconds(5), adapter, "x", "Handle"));
        Assert.Equal("slow", await NativeAdapterRuntime.Within(Delayed("slow", 300), null, adapter, "x", "Handle"));
        Assert.False(adapter.Disposed);

        // The adapter's own failure is its own, not a timeout.
        var own = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NativeAdapterRuntime.Within(Task.FromException<object>(new InvalidOperationException("refused")), TimeSpan.FromSeconds(5), adapter, "x", "Handle"));
        Assert.Equal("refused", own.Message);
    }

    static async Task<object> Delayed(string value, int ms)
    {
        await Task.Delay(ms);
        return value;
    }
}
