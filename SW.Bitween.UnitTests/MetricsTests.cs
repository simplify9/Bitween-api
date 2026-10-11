using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Domain.Metrics;
using SW.Bitween.Services.Adapters;
using SW.Bitween.Services.Metrics;

namespace SW.Bitween.UnitTests;

/// <summary>
/// The in-memory half of the metrics: what is counted, how durations become percentiles, how errors
/// are grouped, and that nothing counted is lost when a flush takes the counts mid-stream.
/// </summary>
[TestClass]
public class MetricsTests
{
    [TestMethod]
    public void A_percentile_is_the_upper_edge_of_its_bucket_and_never_more_than_the_longest_seen()
    {
        var stats = new ExchangeStats();
        for (var i = 0; i < 90; i++) stats.AddDuration(40);    // < 50 ms
        for (var i = 0; i < 9; i++) stats.AddDuration(800);    // < 1 s
        stats.AddDuration(45_000);                              // past every edge

        Assert.AreEqual(50, DurationBuckets.Percentile(stats.Buckets(), 0.5, stats.DurationMaxMs));
        Assert.AreEqual(1_000, DurationBuckets.Percentile(stats.Buckets(), 0.95, stats.DurationMaxMs));
        Assert.AreEqual(45_000, DurationBuckets.Percentile(stats.Buckets(), 1.0, stats.DurationMaxMs));
        Assert.IsNull(DurationBuckets.Percentile(new long[10], 0.95, 0));

        // A bucket's edge says "under 50 ms"; when everything took 3 ms, it says 3.
        var quick = new ExchangeStats();
        quick.AddDuration(3);
        Assert.AreEqual(3, DurationBuckets.Percentile(quick.Buckets(), 0.95, quick.DurationMaxMs));
    }

    [TestMethod]
    public void Errors_that_differ_only_in_their_details_are_one_kind()
    {
        var a = ErrorFingerprint.Of("Order 1043 not found at https://acme.test/orders/1043 (id 7c9e6679-7425-40de-944b-e07fc1f90ae7)");
        var b = ErrorFingerprint.Of("Order 2210 not found at https://acme.test/orders/2210 (id 0f8fad5b-d9cb-469f-a165-70867728950e)");
        var c = ErrorFingerprint.Of("Connection refused");
        Assert.AreEqual(a, b);
        Assert.AreNotEqual(a, c);
        Assert.AreEqual("first line", ErrorFingerprint.Sample("\n  first line \n   at Some.Stack()"));
    }

    [TestMethod]
    public void Exchanges_are_counted_by_hour_subscription_and_partner()
    {
        var recorder = new MetricsRecorder();
        var started = new DateTime(2026, 10, 11, 9, 15, 0, DateTimeKind.Utc);
        recorder.Exchange(started, 5, 7, ExchangeOutcome.Succeeded, FailedAt.Elsewhere, false, false, 120, 1000, 400, null);
        recorder.Exchange(started.AddMinutes(30), 5, 7, ExchangeOutcome.Failed, FailedAt.Handler, true, false, 900, 1000, 0, "Timed out talking to Acme");
        recorder.Exchange(started.AddHours(1), 5, 7, ExchangeOutcome.BadResponse, FailedAt.Elsewhere, false, true, 300, 10, 20, null);

        var snapshot = recorder.Drain();
        var nine = snapshot.Exchanges.Single(s => s.Hour.Hour == 9);
        Assert.AreEqual((2, 1, 1, 1, 1), (nine.Total, nine.Succeeded, nine.Failed, nine.FailedAtHandler, nine.Retries));
        Assert.AreEqual((2000L, 400L), (nine.BytesIn, nine.BytesOut));
        var ten = snapshot.Exchanges.Single(s => s.Hour.Hour == 10);
        Assert.AreEqual((1, 1, 1), (ten.Total, ten.BadResponse, ten.ManualRetries));
        Assert.AreEqual("5", snapshot.Errors.Single().ScopeKey);
        Assert.IsTrue(recorder.Drain().IsEmpty, "drained counts are gone from the recorder");
    }

    [TestMethod]
    public async Task Nothing_counted_while_a_flush_drains_is_lost()
    {
        var recorder = new MetricsRecorder();
        var started = DateTime.UtcNow;
        var counted = new List<MetricsRecorder.Snapshot>();
        var writers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 5_000; i++)
                recorder.Exchange(started, 1, 0, ExchangeOutcome.Succeeded, FailedAt.Elsewhere, false, false, 10, 1, 1, null);
        })).ToArray();
        while (!writers.All(w => w.IsCompleted))
        {
            counted.Add(recorder.Drain());
            await Task.Delay(1);
        }
        counted.Add(recorder.Drain());
        Assert.AreEqual(40_000, counted.SelectMany(s => s.Exchanges).Sum(s => s.Total));
    }

    [TestMethod]
    public void Counts_a_flush_could_not_write_go_with_the_next_one()
    {
        var recorder = new MetricsRecorder();
        recorder.Exchange(DateTime.UtcNow, 3, 0, ExchangeOutcome.Succeeded, FailedAt.Elsewhere, false, false, 10, 1, 1, null);
        var failed = recorder.Drain();
        recorder.Exchange(DateTime.UtcNow, 3, 0, ExchangeOutcome.Succeeded, FailedAt.Elsewhere, false, false, 10, 1, 1, null);
        recorder.Restore(failed);
        Assert.AreEqual(2, recorder.Drain().Exchanges.Single().Total);
    }

    [TestMethod]
    public void A_failed_adapter_call_is_told_apart_by_how_it_failed()
    {
        Assert.AreEqual(AdapterFailure.None, AdapterInvoker.Classify(null));
        Assert.AreEqual(AdapterFailure.Timeout, AdapterInvoker.Classify(new TimeoutException()));
        Assert.AreEqual(AdapterFailure.Crash, AdapterInvoker.Classify(new IOException("Adapter stream closed.")));
        Assert.AreEqual(AdapterFailure.Crash, AdapterInvoker.Classify(new Exception("Process not started or terminated.")));
        Assert.AreEqual(AdapterFailure.Error, AdapterInvoker.Classify(new InvalidOperationException("Partner said no")));
    }

    [TestMethod]
    public async Task Every_adapter_call_is_timed_and_counted_with_its_runtime_role_and_version()
    {
        var recorder = new MetricsRecorder();
        var invoker = new AdapterInvoker([new ThrowingRuntime()], recorder);

        await invoker.InvokeAsync<object>("Acme.Handlers.Orders/1.2.0", AdapterRole.Handler, "Handle", null);
        await Assert.ThrowsExceptionAsync<TimeoutException>(() =>
            invoker.InvokeAsync<object>("acme.handlers.orders/1.2.0", AdapterRole.Handler, "Slow", null));

        var row = recorder.Drain().Adapters.Single();
        Assert.AreEqual(("acme.handlers.orders", "1.2.0", "handler"), (row.AdapterId, row.Version, row.Role));
        Assert.AreEqual((2, 1, 1), (row.Calls, row.Failures, row.Timeouts));
    }

    sealed class ThrowingRuntime : IAdapterRuntime
    {
        public Task<bool> CanRunAsync(string adapterId) => Task.FromResult(true);

        public Task<IAdapterSession> BeginAsync(string adapterId, AdapterRole role, IDictionary<string, string> properties,
            string correlationId) => Task.FromResult<IAdapterSession>(new Session());

        sealed class Session : IAdapterSession
        {
            public Task<TResult> InvokeAsync<TResult>(string method, object argument = null) =>
                method == "Slow" ? throw new TimeoutException("too slow") : Task.FromResult(default(TResult));

            public Task InvokeAsync(string method, object argument = null) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
