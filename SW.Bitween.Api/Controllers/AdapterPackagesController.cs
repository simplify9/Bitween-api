using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Services.Adapters;
using SW.PrimitiveTypes;

namespace SW.Bitween.Controllers;

/// <summary>
/// Publishing a package through Bitween: what bitween adapter publish does by default, so the people
/// who write adapters need a Bitween account rather than the storage's keys, and every version
/// published is in the audit trail with who published it.
/// </summary>
/// <remarks>
/// A controller rather than a CqApi handler because the package is the request body itself — the zip
/// bytes, as built — not JSON. Refusals answer as CqApi's do: 403 without the permission, 400 with
/// <c>{ field: [message] }</c>.
/// </remarks>
[ApiController]
[Route("api/adapters/packages")]
public class AdapterPackagesController(BitweenDbContext dbContext, RequestContext requestContext, AdapterWorkshop workshop)
    : ControllerBase
{
    /// <param name="version">major, minor, patch or an exact version; the package's own when empty.</param>
    /// <param name="current">Make it the version that runs where nothing is pinned.</param>
    /// <param name="releaseNotes">Replaces the package's own, when given.</param>
    [HttpPost]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<IActionResult> Post([FromQuery] string version, [FromQuery] bool current, [FromQuery] string releaseNotes)
    {
        try
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.AdapterSource.Operate);
        }
        catch (SWUnauthorizedException)
        {
            return StatusCode(403);
        }

        var who = requestContext.GetNameIdentifier();
        var account = int.TryParse(who, out var accountId)
            ? await dbContext.Set<Domain.Accounts.Account>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId)
            : null;

        try
        {
            var published = await workshop.UploadAsync(Request.Body, version, current, releaseNotes, account?.DisplayName ?? who);
            dbContext.Add(new AdapterRelease(published.Manifest.Id, published.Version, AdapterRelease.PublishedAction, null, who));
            if (current)
                dbContext.Add(new AdapterRelease(published.Manifest.Id, published.Version, AdapterRelease.PromotedAction, null, who));
            await dbContext.SaveChangesAsync();
            return Ok(new { AdapterId = published.Manifest.Id, published.Version, Current = current, published.Sha256 });
        }
        catch (SWValidationException ex)
        {
            foreach (var v in ex.Validations) ModelState.AddModelError(v.Key, v.Value);
            return BadRequest(ModelState);
        }
        catch (SWException ex)
        {
            ModelState.AddModelError("Package", ex.Message);
            return BadRequest(ModelState);
        }
    }
}
