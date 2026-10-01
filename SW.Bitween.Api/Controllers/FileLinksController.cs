using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SW.PrimitiveTypes;

namespace SW.Bitween.Controllers;

/// <summary>
/// Serves one exchange file to whoever holds its link (see <see cref="FileLinks"/>): no login, the way
/// the public storage URL it stands in for worked, so a reader built for those links needs no change.
/// </summary>
/// <remarks>
/// The literal "files" keeps this clear of CqApi, which owns the rest of /api/ — see
/// <see cref="GatewayController"/>.
/// </remarks>
[ApiController]
[Route(FileLinks.RoutePrefix)]
public class FileLinksController(FileLinks links, ICloudFilesService cloudFiles, StorageRetention retention)
    : ControllerBase
{
    [HttpGet("{seal}/{**key}")]
    public async Task<IActionResult> Get([FromRoute] string seal, [FromRoute] string key)
    {
        // The same answer for a bad seal as for a missing file, so a link can't be used to probe
        // which exchanges exist.
        if (!links.Opens(seal, key)) return NotFound();

        try
        {
            var stream = await cloudFiles.OpenReadAsync(key);
            // What storage served these files as: they're written without a content type of their own.
            return File(stream, "text/plain");
        }
        catch (Exception ex) when (StorageErrors.IsNotFound(ex))
        {
            var rule = (await retention.GetAsync()).RuleFor(key);
            return NotFound(rule != null
                ? $"This file was deleted by the storage retention policy: files under {rule.Prefix} are kept {rule.Days} days."
                : "This file isn't in storage.");
        }
    }
}
