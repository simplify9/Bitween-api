using System.Linq;
using System.Threading.Tasks;
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
        if (work.Start(MirrorWork.Sampling, (m, progress, ct) => m.TakeSamplesAsync(progress, ct)) == MirrorWork.Rehearsing)
            throw new SWValidationException("MIRROR_BUSY", "A rehearsal is running on the samples there are. Take samples once it finishes.");
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
        if (work.Start(MirrorWork.Rehearsing, (m, progress, ct) => m.RehearseAsync(progress, token, ct)) == MirrorWork.Sampling)
            throw new SWValidationException("MIRROR_BUSY", "Samples are being taken. Rehearse once that finishes.");
        return await mirror.RehearsalAsync(work);
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
