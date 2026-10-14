using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SW.Bitween.Domain;
using SW.Bitween.Services.Marketplace;
using SW.PrimitiveTypes;

namespace SW.Bitween.Controllers;

/// <summary>
/// Install from bundle: the zip bitween registry bundle downloaded, for a Bitween that can't reach
/// the registry. A controller because the bundle is the request body itself. Needs the trusted keys
/// in Settings, not the marketplace to be on: an offline site turns the marketplace off.
/// </summary>
[ApiController]
[Route("api/marketplace/bundle")]
public class MarketplaceBundleController(BitweenDbContext dbContext, RequestContext requestContext, MarketplaceInstaller installer)
    : ControllerBase
{
    /// <param name="current">Make each installed version the one that runs where nothing is pinned.</param>
    [HttpPost]
    [RequestSizeLimit(600 * 1024 * 1024)]
    public async Task<IActionResult> Post([FromQuery] bool current)
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
        try
        {
            var result = await installer.InstallBundleAsync(Request.Body, current, who);
            foreach (var item in result.Items.Where(i => i.Outcome == "Installed"))
            {
                dbContext.Add(new AdapterRelease(item.Id, item.Version, AdapterRelease.InstalledAction, null, who));
                if (current) dbContext.Add(new AdapterRelease(item.Id, item.Version, AdapterRelease.PromotedAction, null, who));
            }
            await dbContext.SaveChangesAsync();
            return Ok(result);
        }
        catch (SWValidationException ex)
        {
            foreach (var v in ex.Validations) ModelState.AddModelError(v.Key, v.Value);
            return BadRequest(ModelState);
        }
        catch (SWException ex)
        {
            ModelState.AddModelError("Bundle", ex.Message);
            return BadRequest(ModelState);
        }
    }
}
