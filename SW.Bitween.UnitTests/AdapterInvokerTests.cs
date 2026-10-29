using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests;

/// <summary>
/// Which runtime runs an adapter.
///
/// There are three - in-process, a spawned process, a pooled long-lived one - and they are not
/// interchangeable: sending a resident adapter down the classic path does not fail cleanly, it
/// waits out the command timeout for an answer that will never come. Choosing correctly is
/// therefore not a detail, and it is made here rather than at every call site so there is one
/// place to get it right and one place to test.
///
/// No database, no processes, no containers: the whole point of the seam is that this is testable
/// with a fake.
/// </summary>
[TestClass]
public class AdapterInvokerTests
{
    [TestMethod]
    public async Task The_first_runtime_that_claims_an_adapter_runs_it()
    {
        var native = new FakeRuntime("native", claims: id => id.StartsWith("native."));
        var resident = new FakeRuntime("resident", claims: id => id.StartsWith("res."));
        var classic = new FakeRuntime("classic", claims: _ => true);

        var invoker = new AdapterInvoker([native, resident, classic]);

        await invoker.InvokeAsync<string>("res.thing", AdapterRole.Handler, "Handle", null);

        Assert.AreEqual(1, resident.Begins);
        Assert.AreEqual(0, native.Begins);
        Assert.AreEqual(0, classic.Begins, "a later runtime ran one an earlier one had claimed");
    }

    /// <summary>
    /// The classic runtime claims everything, so registration order is the routing. If it were
    /// asked first it would swallow every adapter, including the ones it cannot run.
    /// </summary>
    [TestMethod]
    public async Task The_catch_all_runtime_only_sees_what_the_others_declined()
    {
        var resident = new FakeRuntime("resident", claims: id => id.StartsWith("res."));
        var classic = new FakeRuntime("classic", claims: _ => true);

        var invoker = new AdapterInvoker([resident, classic]);

        await invoker.InvokeAsync<string>("something.else", AdapterRole.Mapper, "Handle", null);

        Assert.AreEqual(1, classic.Begins);
        Assert.AreEqual(0, resident.Begins);
    }

    /// <summary>
    /// The role travels with the call. A mapper and a handler are both invoked as Handle, and only
    /// the role tells a native runtime which of the two registrations to resolve - so losing it
    /// here would silently run the wrong adapter.
    /// </summary>
    [TestMethod]
    public async Task The_role_reaches_the_runtime()
    {
        var runtime = new FakeRuntime("only", claims: _ => true);
        var invoker = new AdapterInvoker([runtime]);

        await invoker.InvokeAsync<string>("x", AdapterRole.Validator, "Validate", null);

        Assert.AreEqual(AdapterRole.Validator, runtime.LastRole);
    }

    [TestMethod]
    public async Task Properties_and_the_correlation_id_reach_the_runtime()
    {
        var runtime = new FakeRuntime("only", claims: _ => true);
        var invoker = new AdapterInvoker([runtime]);

        var properties = new Dictionary<string, string> { ["Host"] = "broker" };
        await invoker.InvokeAsync<string>("x", AdapterRole.Handler, "Handle", null, properties, "corr-1");

        Assert.AreEqual("broker", runtime.LastProperties?["Host"]);
        Assert.AreEqual("corr-1", runtime.LastCorrelationId);
    }

    /// <summary>
    /// A one-call invoke has to release the session. For a resident adapter the session IS a
    /// pooled lease, so leaking one takes a warm instance out of circulation for good - and the
    /// pool runs dry with no error to say why.
    /// </summary>
    [TestMethod]
    public async Task A_single_call_still_releases_the_session()
    {
        var runtime = new FakeRuntime("only", claims: _ => true);
        var invoker = new AdapterInvoker([runtime]);

        await invoker.InvokeAsync<string>("x", AdapterRole.Handler, "Handle", null);

        Assert.IsTrue(runtime.LastSession.Disposed, "the session was not released");
    }

    /// <summary>A session held by the caller is theirs to release, and is not disposed early.</summary>
    [TestMethod]
    public async Task A_session_stays_open_until_the_caller_disposes_it()
    {
        var runtime = new FakeRuntime("only", claims: _ => true);
        var invoker = new AdapterInvoker([runtime]);

        var session = await invoker.BeginAsync("x", AdapterRole.Receiver);
        await session.InvokeAsync("Initialize");
        await session.InvokeAsync<List<string>>("ListFiles");

        Assert.IsFalse(runtime.LastSession.Disposed);

        await session.DisposeAsync();
        Assert.IsTrue(runtime.LastSession.Disposed);

        // Every call in the session went to ONE instance, which is what a receiver depends on.
        Assert.AreEqual(1, runtime.Begins);
        CollectionAssert.AreEqual(new[] { "Initialize", "ListFiles" }, runtime.LastSession.Calls);
    }

    [TestMethod]
    public async Task An_adapter_no_runtime_claims_is_a_clear_error()
    {
        var invoker = new AdapterInvoker([new FakeRuntime("picky", claims: _ => false)]);

        var error = await Assert.ThrowsExceptionAsync<BitweenException>(
            () => invoker.InvokeAsync<string>("orphan", AdapterRole.Handler, "Handle", null));

        StringAssert.Contains(error.Message, "orphan");
    }

    // ---------------------------------------------------------------- fakes

    private sealed class FakeRuntime(string name, Func<string, bool> claims) : IAdapterRuntime
    {
        public int Begins { get; private set; }
        public AdapterRole LastRole { get; private set; }
        public IDictionary<string, string> LastProperties { get; private set; }
        public string LastCorrelationId { get; private set; }
        public FakeSession LastSession { get; private set; }

        public override string ToString() => name;

        public Task<bool> CanRunAsync(string adapterId) => Task.FromResult(claims(adapterId));

        public Task<IAdapterSession> BeginAsync(string adapterId, AdapterRole role,
            IDictionary<string, string> properties, string correlationId)
        {
            Begins++;
            LastRole = role;
            LastProperties = properties;
            LastCorrelationId = correlationId;
            LastSession = new FakeSession();
            return Task.FromResult<IAdapterSession>(LastSession);
        }
    }

    private sealed class FakeSession : IAdapterSession
    {
        public List<string> Calls { get; } = [];
        public bool Disposed { get; private set; }

        public Task<TResult> InvokeAsync<TResult>(string method, object argument = null)
        {
            Calls.Add(method);
            return Task.FromResult(default(TResult));
        }

        public Task InvokeAsync(string method, object argument = null)
        {
            Calls.Add(method);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
