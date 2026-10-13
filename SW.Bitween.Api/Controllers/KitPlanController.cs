using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SW.Bitween.Services.Marketplace;
using SW.PrimitiveTypes;

namespace SW.Bitween.Controllers;

/// <summary>
/// Plans a kit sent as its zip, changing nothing: what installing it here would do, or why it can't be.
/// The registry calls it on a reference Bitween for every kit published, so a kit whose bundle doesn't
/// plan never reaches review; a publisher can call it on their own Bitween too.
/// </summary>
/// <remarks>
/// A controller, as <see cref="AdapterPackagesController"/> is: the zip is the request body. It plans as
/// the caller, through this Bitween's own API, and reads nothing but the kit's own files and parameters.
/// </remarks>
[ApiController]
[Route("api/kits/plan")]
public class KitPlanController(BitweenDbContext dbContext, RequestContext requestContext, KitInstaller kits) : ControllerBase
{
    /// <summary>The largest kit planned: a kit is YAML and a few schemas, never a package's binaries.</summary>
    const int MaxKitBytes = 20 * 1024 * 1024;

    /// <param name="name">What the installation would be called; the kit's id when empty.</param>
    /// <param name="examples">A required parameter not given takes the kit's example, or a placeholder.</param>
    /// <param name="set">Parameters as NAME=value, repeated.</param>
    [HttpPost]
    [RequestSizeLimit(MaxKitBytes)]
    public async Task<IActionResult> Post([FromQuery] string name, [FromQuery] bool examples, [FromQuery] string[] set)
    {
        try
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.AdapterSource.Operate);
        }
        catch (SWUnauthorizedException)
        {
            return StatusCode(403);
        }

        var parameters = new Dictionary<string, string>();
        foreach (var pair in set ?? [])
        {
            var at = pair.IndexOf('=');
            if (at <= 0)
            {
                ModelState.AddModelError("set", $"{pair}: give a parameter as NAME=value.");
                return BadRequest(ModelState);
            }
            parameters[pair[..at].Trim()] = pair[(at + 1)..];
        }

        using var body = new MemoryStream();
        await Request.Body.CopyToAsync(body);
        try
        {
            return Ok(await kits.PlanPackageAsync(body.ToArray(), name, parameters, examples));
        }
        catch (SWValidationException ex)
        {
            foreach (var v in ex.Validations) ModelState.AddModelError(v.Key, v.Value);
            return BadRequest(ModelState);
        }
    }
}
