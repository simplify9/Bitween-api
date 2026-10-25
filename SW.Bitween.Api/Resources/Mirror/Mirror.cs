using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Services.Mirror;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Mirror;

/// <summary>Whether this Bitween is a mirror and how it reaches prod, without calling prod: the page's first answer.</summary>
public class Get(BitweenDbContext dbContext, RequestContext requestContext, BitweenOptions options) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Mirror.View);
        var m = options.Mirror;
        var signIn = !string.IsNullOrWhiteSpace(m.SourceToken) ? "token"
            : !string.IsNullOrWhiteSpace(m.SourceEmail) && !string.IsNullOrEmpty(m.SourcePassword) ? "member"
            : null;
        return new
        {
            m.Target,
            m.SourceUrl,
            SignIn = signIn,
            Configured = !string.IsNullOrWhiteSpace(m.SourceUrl) && signIn != null,
        };
    }
}

/// <summary>
/// How close this staging is to prod, read from both now: per kind what is the same, differs, is
/// missing or is only here; adapters; the mock addresses that stand in for prod's; and what is left to
/// do. Prod is only read. A prod that can't be reached, or is too old, answers with why.
/// </summary>
[HandlerName("parity")]
public class Parity(BitweenDbContext dbContext, RequestContext requestContext, MirrorService mirror) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Mirror.View);
        try
        {
            return await mirror.ParityAsync();
        }
        catch (MirrorRefusal refusal)
        {
            return new { Error = refusal.Message };
        }
    }
}

/// <summary>
/// The samples of prod's exchanges staging holds, faked, each file with a link that opens it, and how a
/// taking under way is going.
/// </summary>
[HandlerName("samples")]
public class Samples(BitweenDbContext dbContext, RequestContext requestContext, MirrorService mirror, MirrorWork work) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Mirror.View);
        return await mirror.SamplesAsync(work);
    }
}

public class MirrorTakeSamplesRequest;

/// <summary>
/// Starts taking samples of prod's exchanges, in the background: a few recent ones for each
/// subscription, faked as they arrive. The page follows it through <see cref="Samples"/>. Only on a
/// Bitween its deployment marks as a mirror, and not while a rehearsal runs on the samples there are.
/// </summary>
[HandlerName("takesamples")]
public class TakeSamples(BitweenDbContext dbContext, RequestContext requestContext, BitweenOptions options, MirrorService mirror, MirrorWork work)
    : ICommandHandler<MirrorTakeSamplesRequest, object>
{
    public async Task<object> Handle(MirrorTakeSamplesRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Mirror.Operate);
        if (!options.Mirror.Target) throw new SWValidationException("MIRROR_NOT_TARGET", MirrorService.NotTarget);
        if (MirrorBusy.Why(work.Start(MirrorWork.Sampling, (m, progress, ct) => m.TakeSamplesAsync(progress, ct)), MirrorWork.Sampling) is { } busy)
            throw new SWValidationException("MIRROR_BUSY", busy);
        return await mirror.SamplesAsync(work);
    }
}

/// <summary>
/// The last rehearsal: each of prod's samples run on staging, with the exchange it made and how it
/// compares with prod's. Followed by the page while one runs.
/// </summary>
[HandlerName("rehearsal")]
public class Rehearsal(BitweenDbContext dbContext, RequestContext requestContext, MirrorService mirror, MirrorWork work) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Mirror.View);
        return await mirror.RehearsalAsync(work);
    }
}

public class MirrorRehearseRequest;

/// <summary>
/// Starts a rehearsal, in the background: every sample run on staging as a real exchange, then compared
/// with how prod's went. Only on a Bitween its deployment marks as a mirror, and not while samples are
/// being taken.
/// </summary>
[HandlerName("rehearse")]
public class Rehearse(BitweenDbContext dbContext, RequestContext requestContext, BitweenOptions options, MirrorService mirror, MirrorWork work)
    : ICommandHandler<MirrorRehearseRequest, object>
{
    public async Task<object> Handle(MirrorRehearseRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Mirror.Operate);
        if (!options.Mirror.Target) throw new SWValidationException("MIRROR_NOT_TARGET", MirrorService.NotTarget);
        var token = mirror.CallerToken();
        if (MirrorBusy.Why(work.Start(MirrorWork.Rehearsing, (m, progress, ct) => m.RehearseAsync(progress, token, ct)), MirrorWork.Rehearsing) is { } busy)
            throw new SWValidationException("MIRROR_BUSY", busy);
        return await mirror.RehearsalAsync(work);
    }
}

/// <summary>Why the mirror's work can't start while other work runs.</summary>
static class MirrorBusy
{
    /// <returns>Null when nothing else runs, or the work under way is the same work, which then carries on.</returns>
    public static string Why(MirrorProgress running, string kind, string subject = null) =>
        running == null || (running.Kind == kind && running.Subject == subject) ? null
        : $"{MirrorWork.Describe(running)} is under way. Start this once it finishes.";
}

public class MirrorSubscriptionRequest
{
    /// <summary>The subscription, by its id on this staging.</summary>
    public int Id { get; set; }

    /// <summary>Its name, which is what prod's is found by: one this staging doesn't have is refused.</summary>
    internal static async Task<string> NameAsync(BitweenDbContext dbContext, MirrorSubscriptionRequest request) =>
        await dbContext.Subscriptions().AsNoTracking().Where(s => s.Id == request.Id).Select(s => s.Name).FirstOrDefaultAsync()
        ?? throw new SWNotFoundException($"There's no subscription {request?.Id} on this staging.");
}

/// <summary>
/// One of this staging's subscriptions beside prod's of the same name: it and each item it needs to run
/// as on prod, the same, faked, different field by field or not on staging; what bringing it would do; and
/// what only a person can do first. Prod is only read; one that can't be read answers <c>{ error }</c>.
/// </summary>
[HandlerName("subscription")]
public class Subscription(BitweenDbContext dbContext, RequestContext requestContext, MirrorService mirror)
    : IQueryHandler<MirrorSubscriptionRequest, object>
{
    public async Task<object> Handle(MirrorSubscriptionRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Mirror.View);
        var name = await MirrorSubscriptionRequest.NameAsync(dbContext, request);
        try
        {
            return await mirror.SubscriptionAsync(name);
        }
        catch (MirrorRefusal refusal)
        {
            return new { Error = refusal.Message };
        }
    }
}

/// <summary>
/// A subscription's test with prod's traffic: prod's latest arrivals it takes part in, how they last ran
/// on staging, and how work for it under way is going. Followed by the page while it runs.
/// </summary>
[HandlerName("test")]
public class Test(BitweenDbContext dbContext, RequestContext requestContext, MirrorService mirror, MirrorWork work)
    : IQueryHandler<MirrorSubscriptionRequest, object>
{
    public async Task<object> Handle(MirrorSubscriptionRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Mirror.View);
        return await mirror.TestAsync(await MirrorSubscriptionRequest.NameAsync(dbContext, request), work);
    }
}

/// <summary>
/// Starts taking prod's latest arrivals a subscription takes part in, faked, in the background; they
/// replace the ones taken before. Only on a Bitween its deployment marks as a mirror, and not while other
/// work of the mirror runs.
/// </summary>
[HandlerName("testtake")]
public class TestTake(BitweenDbContext dbContext, RequestContext requestContext, BitweenOptions options, MirrorService mirror, MirrorWork work)
    : ICommandHandler<MirrorSubscriptionRequest, object>
{
    public async Task<object> Handle(MirrorSubscriptionRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Mirror.Operate);
        if (!options.Mirror.Target) throw new SWValidationException("MIRROR_NOT_TARGET", MirrorService.NotTarget);
        var name = await MirrorSubscriptionRequest.NameAsync(dbContext, request);
        if (MirrorBusy.Why(work.Start(MirrorWork.Sampling, (m, progress, ct) => m.TakeTestAsync(name, progress, ct), name), MirrorWork.Sampling, name) is { } busy)
            throw new SWValidationException("MIRROR_BUSY", busy);
        return await mirror.TestAsync(name, work);
    }
}

/// <summary>
/// Starts a subscription's test in the background: each of prod's arrivals sent in on staging the way it
/// came in on prod, and every exchange it led to compared with its twin. Only on a Bitween its deployment
/// marks as a mirror, and not while other work of the mirror runs.
/// </summary>
[HandlerName("testrun")]
public class TestRun(BitweenDbContext dbContext, RequestContext requestContext, BitweenOptions options, MirrorService mirror, MirrorWork work)
    : ICommandHandler<MirrorSubscriptionRequest, object>
{
    public async Task<object> Handle(MirrorSubscriptionRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Mirror.Operate);
        if (!options.Mirror.Target) throw new SWValidationException("MIRROR_NOT_TARGET", MirrorService.NotTarget);
        var name = await MirrorSubscriptionRequest.NameAsync(dbContext, request);
        var token = mirror.CallerToken();
        if (MirrorBusy.Why(work.Start(MirrorWork.Rehearsing, (m, progress, ct) => m.RunTestAsync(name, progress, token, ct), name), MirrorWork.Rehearsing, name) is { } busy)
            throw new SWValidationException("MIRROR_BUSY", busy);
        return await mirror.TestAsync(name, work);
    }
}

/// <summary>
/// Removes what a subscription's test made on staging: every exchange its runs started, with their files,
/// and how they compared. Prod's arrivals stay, to run again. Not while the test runs.
/// </summary>
[HandlerName("testclear")]
public class TestClear(BitweenDbContext dbContext, RequestContext requestContext, BitweenOptions options, MirrorService mirror, MirrorWork work)
    : ICommandHandler<MirrorSubscriptionRequest, object>
{
    public async Task<object> Handle(MirrorSubscriptionRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Mirror.Operate);
        if (!options.Mirror.Target) throw new SWValidationException("MIRROR_NOT_TARGET", MirrorService.NotTarget);
        var name = await MirrorSubscriptionRequest.NameAsync(dbContext, request);
        if (work.Running?.Subject == name)
            throw new SWValidationException("MIRROR_BUSY", "The test is running. Clear what it made once it finishes.");
        await mirror.ClearTestAsync(name);
        return await mirror.TestAsync(name, work);
    }
}

/// <summary>
/// The calls staging answered in place of the addresses they were for, since this node started: by
/// host, and the latest ones. Empty on a Bitween that isn't a mirror, which answers none.
/// </summary>
[HandlerName("calls")]
public class Calls(BitweenDbContext dbContext, RequestContext requestContext, BitweenOptions options, MirrorMock mock) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Mirror.View);
        return new
        {
            Catching = options.Mirror.Target,
            Hosts = options.Mirror.Target ? await mock.HostsAsync() : [],
            Recent = mock.Recent.Take(50).ToList(),
        };
    }
}

/// <summary>
/// How many of prod's items there are of each kind a page lists - subscriptions by type, API and bus
/// gateways - so each page offers to bring only what prod has. Read from prod's lists alone.
/// </summary>
[HandlerName("prodkinds")]
public class ProdKinds(BitweenDbContext dbContext, RequestContext requestContext, MirrorService mirror) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Mirror.View);
        return await mirror.ProdKindsAsync();
    }
}

public class MirrorProdRequest
{
    /// <summary>subscriptions, apiGateways or busGateways.</summary>
    public string Section { get; set; }

    /// <summary>Of subscriptions, only those of this type: Receiving, Aggregation or Response, say.</summary>
    public string Type { get; set; }
}

/// <summary>
/// Prod's items of one kind, busiest first, each with its traffic over the last week and whether staging
/// has it: what a page offers to bring. Prod is only read.
/// </summary>
[HandlerName("prod")]
public class Prod(BitweenDbContext dbContext, RequestContext requestContext, MirrorService mirror) : IQueryHandler<MirrorProdRequest, object>
{
    public async Task<object> Handle(MirrorProdRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Mirror.View);
        try
        {
            return await mirror.ProdListAsync(request?.Section, string.IsNullOrWhiteSpace(request?.Type) ? null : request.Type);
        }
        catch (MirrorRefusal refusal)
        {
            return new { Error = refusal.Message };
        }
    }
}

public class MirrorBringRequest
{
    /// <summary>subscriptions, apiGateways or busGateways.</summary>
    public string Section { get; set; }

    /// <summary>The picked items, by the name prod has them under (a gateway's address for an API gateway).</summary>
    public List<string> Keys { get; set; }

    /// <summary>Bring them; without it, only say what bringing them would do.</summary>
    public bool Apply { get; set; }
}

/// <summary>
/// Brings prod's picked items onto this staging with what they need to run as on prod, faked: what staging
/// doesn't have comes as prod has it, what it has stays as it is, and nothing is removed. Without
/// <see cref="MirrorBringRequest.Apply"/> it says what it would do. Only on a Bitween its deployment marks
/// as a mirror.
/// </summary>
[HandlerName("bring")]
public class Bring(BitweenDbContext dbContext, RequestContext requestContext, BitweenOptions options, MirrorService mirror)
    : ICommandHandler<MirrorBringRequest, object>
{
    public async Task<object> Handle(MirrorBringRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Mirror.Operate);
        if (!options.Mirror.Target) throw new SWValidationException("MIRROR_NOT_TARGET", MirrorService.NotTarget);
        try
        {
            return await mirror.BringAsync(request?.Section, request?.Keys ?? [], request?.Apply ?? false);
        }
        catch (MirrorRefusal refusal)
        {
            throw new SWValidationException("MIRROR", refusal.Message);
        }
    }
}

public class MirrorCopyRequest
{
    /// <summary>Also remove what staging has and prod doesn't. Off unless asked, since it deletes.</summary>
    public bool RemoveExtras { get; set; }
}

/// <summary>
/// Copies prod's setup onto this staging, faked: creates what is missing, updates what differs, and
/// with <see cref="MirrorCopyRequest.RemoveExtras"/> removes what prod doesn't have. Carries on past an
/// item that fails, and says which and why. Only on a Bitween its deployment marks as a mirror.
/// </summary>
[HandlerName("copy")]
public class Copy(BitweenDbContext dbContext, RequestContext requestContext, MirrorService mirror) : ICommandHandler<MirrorCopyRequest, object>
{
    public async Task<object> Handle(MirrorCopyRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Mirror.Operate);
        try
        {
            return await mirror.CopyAsync(request?.RemoveExtras ?? false);
        }
        catch (MirrorRefusal refusal)
        {
            throw new SWValidationException("MIRROR", refusal.Message);
        }
    }
}
