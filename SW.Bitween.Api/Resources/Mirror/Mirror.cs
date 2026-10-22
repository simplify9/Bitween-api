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
