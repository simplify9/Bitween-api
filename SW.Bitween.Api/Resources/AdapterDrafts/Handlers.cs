using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.AdapterDrafts;

static class Drafts
{
    public static object Summary(AdapterDraft d) => new
    {
        d.Id, d.AdapterId, d.Language, d.Kind, d.BaseVersion, d.FilesHash,
        d.CreatedOn, d.CreatedBy, d.ModifiedOn, d.ModifiedBy,
    };

    public static object Full(AdapterDraft d) => new
    {
        d.Id, d.AdapterId, d.Language, d.Kind, d.BaseVersion, d.FilesHash,
        d.CreatedOn, d.CreatedBy, d.ModifiedOn, d.ModifiedBy,
        d.Files,
    };

    public static async Task<AdapterDraft> Find(BitweenDbContext db, int key) =>
        await db.Set<AdapterDraft>().FindAsync(key) ?? throw new SWNotFoundException($"Adapter draft {key}");
}

/// <summary>Every draft in the editor, newest first.</summary>
public class Search(BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<AdapterDraftSearch, object>
{
    public async Task<object> Handle(AdapterDraftSearch request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.AdapterSource.Edit);
        var drafts = await dbContext.Set<AdapterDraft>().AsNoTracking()
            .Where(d => request.AdapterId == null || d.AdapterId == request.AdapterId)
            .OrderByDescending(d => d.ModifiedOn ?? d.CreatedOn)
            .ToListAsync();
        return drafts.Select(Drafts.Summary);
    }
}

public class AdapterDraftSearch
{
    public string AdapterId { get; set; }
}

public class Get(BitweenDbContext dbContext, RequestContext requestContext) : IGetHandler<int, object>
{
    public async Task<object> Handle(int key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.AdapterSource.Edit);
        return Drafts.Full(await Drafts.Find(dbContext, key));
    }
}

/// <summary>Starts a draft: a new adapter from the template, or a published version's source.</summary>
public class Create(BitweenDbContext dbContext, RequestContext requestContext, AdapterWorkshop workshop)
    : ICommandHandler<AdapterDraftCreate, object>
{
    public async Task<object> Handle(AdapterDraftCreate request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.AdapterSource.Edit);
        var draft = !string.IsNullOrWhiteSpace(request.FromAdapterId)
            ? await workshop.FromVersionAsync(request.FromAdapterId.ToLowerInvariant(), request.FromVersion)
            : await workshop.NewAsync(request.Name, request.Language, request.Kind ?? "handler", request.AdapterId);
        dbContext.Add(draft);
        await dbContext.SaveChangesAsync();
        return draft.Id;
    }
}

/// <summary>Saves the draft's files.</summary>
public class Update(BitweenDbContext dbContext, RequestContext requestContext) : ICommandHandler<int, AdapterDraftUpdate, object>
{
    public async Task<object> Handle(int key, AdapterDraftUpdate request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.AdapterSource.Edit);
        var draft = await Drafts.Find(dbContext, key);
        draft.SetFiles(request.Files);
        await dbContext.SaveChangesAsync();
        return Drafts.Summary(draft);
    }
}

public class Delete(BitweenDbContext dbContext, RequestContext requestContext) : IDeleteHandler<int, object>
{
    public async Task<object> Handle(int key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.AdapterSource.Edit);
        dbContext.Remove(await Drafts.Find(dbContext, key));
        await dbContext.SaveChangesAsync();
        return null;
    }
}

/// <summary>Builds the draft and checks it against its contracts, as serverless test would.</summary>
[HandlerName("build")]
public class Build(BitweenDbContext dbContext, RequestContext requestContext, AdapterWorkshop workshop)
    : ICommandHandler<int, AdapterDraftRun, object>
{
    public async Task<object> Handle(int key, AdapterDraftRun request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.AdapterSource.Edit);
        var draft = await Drafts.Find(dbContext, key);
        var result = await workshop.BuildAsync(draft, request?.Settings, check: request?.BuildOnly != true);
        return Describe(result);
    }

    internal static object Describe(WorkshopBuild result) => new
    {
        result.Succeeded,
        result.Conforms,
        result.Problems,
        result.Warnings,
        result.Checks,
        Settings = result.Manifest?.Properties.Select(p => new { p.Name, p.Description, p.Required, p.Secret, p.Default, p.Type }),
        Commands = result.Manifest == null ? null : CommandsOf(result.Manifest),
        result.Manifest?.Lifecycle,
        result.Manifest?.Kinds,
    };

    // The commands a kind's contract names; the try panel offers these.
    static string[] CommandsOf(Serverless.Contract.Catalog.AdapterManifest manifest) =>
        manifest.Kinds.SelectMany(k => k switch
        {
            "handler" or "mapper" => new[] { "Handle" },
            "validator" => ["Validate"],
            "receiver" => ["Initialize", "ListFiles", "GetFile", "DeleteFile", "Finalize"],
            _ => [],
        }).Distinct().ToArray();
}

/// <summary>Builds the draft and calls one of its commands with the settings and input given.</summary>
[HandlerName("try")]
public class Try(BitweenDbContext dbContext, RequestContext requestContext, AdapterWorkshop workshop)
    : ICommandHandler<int, AdapterDraftRun, object>
{
    public async Task<object> Handle(int key, AdapterDraftRun request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.AdapterSource.Edit);
        var draft = await Drafts.Find(dbContext, key);
        return await workshop.TryAsync(draft, request?.Settings, request?.Command, request?.Input);
    }
}

/// <summary>
/// Publishes the draft as a new version, once it builds and passes its contract checks. Not made
/// current: what runs changes only when someone promotes it, or pins it on a subscription.
/// </summary>
[HandlerName("publish")]
public class Publish(BitweenDbContext dbContext, RequestContext requestContext, AdapterWorkshop workshop)
    : ICommandHandler<int, AdapterDraftPublish, object>
{
    public async Task<object> Handle(int key, AdapterDraftPublish request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.AdapterSource.Operate);
        var draft = await Drafts.Find(dbContext, key);
        var who = requestContext.GetNameIdentifier();
        // The catalog says who published each version; a name reads better there than an id.
        var account = int.TryParse(who, out var accountId)
            ? await dbContext.Set<Domain.Accounts.Account>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId)
            : null;
        var (version, build) = await workshop.PublishAsync(draft, request?.Version, request?.ReleaseNotes, request?.Settings,
            account?.DisplayName ?? who ?? "Bitween");
        if (version == null) return new { Published = false, Build = Build.Describe(build) };

        draft.Published(version);
        dbContext.Add(new AdapterRelease(draft.AdapterId, version, AdapterRelease.PublishedAction, draft.Id, who));
        await dbContext.SaveChangesAsync();
        return new { Published = true, Version = version, Build = Build.Describe(build) };
    }
}
