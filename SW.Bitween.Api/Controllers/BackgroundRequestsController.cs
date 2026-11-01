using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Services.Background;
using SW.PrimitiveTypes;

namespace SW.Bitween.Controllers;

/// <summary>
/// The signed-in user's background requests (see <see cref="BackgroundRequestMiddleware"/>): which
/// there are, the answer of one, and cancelling one. Nobody sees anyone else's.
/// </summary>
/// <remarks>
/// A controller rather than CqApi resources, because an answer goes back exactly as the request it
/// stands in for would have sent it: its status, its content type, its bytes.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/background")]
public class BackgroundRequestsController(BitweenDbContext db, BackgroundRequests requests, ICloudFilesService cloudFiles,
    BackgroundDownloads downloads) : ControllerBase
{
    /// <summary>Those still running, and those that finished within the half hour answers are kept.</summary>
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var owner = Owner;
        if (owner == null) return Unauthorized();
        await requests.ForgetExpiredAsync(db);

        var unfinished = await db.Set<BackgroundRequest>()
            .Where(r => r.Owner == owner &&
                        (r.Status == BackgroundRequestStatus.Waiting || r.Status == BackgroundRequestStatus.Running))
            .ToListAsync();
        await BackgroundRequests.EndStoppedAsync(db, unfinished);

        var now = DateTime.UtcNow;
        var since = now - BackgroundRequests.KeptFor;
        var rows = await db.Set<BackgroundRequest>().AsNoTracking()
            .Where(r => r.Owner == owner && r.StartedOn > now.AddDays(-1) && (r.FinishedOn == null || r.FinishedOn > since))
            .OrderByDescending(r => r.StartedOn)
            .Take(20)
            .Select(r => new
            {
                r.Id, r.Label, r.Page, r.Path, r.Status, r.StartedOn, r.FinishedOn, r.ResultStatus, r.Done, r.Total,
                r.Summary
            })
            .ToListAsync();

        return Ok(rows.Select(r => new
        {
            r.Id,
            r.Label,
            r.Page,
            r.Path,
            Status = r.Status.ToString(),
            r.StartedOn,
            r.FinishedOn,
            r.ResultStatus,
            r.Done,
            r.Total,
            r.Summary,
        }));
    }

    /// <summary>The answer, as the request would have given it; or why there is none.</summary>
    [HttpGet("{id}/result")]
    public async Task<IActionResult> Result(string id)
    {
        var owner = Owner;
        if (owner == null) return Unauthorized();
        await requests.ForgetExpiredAsync(db);

        var row = await db.Set<BackgroundRequest>().FirstOrDefaultAsync(r => r.Id == id && r.Owner == owner);
        if (row == null)
            return Refusal(StatusCodes.Status404NotFound, "BACKGROUND_EXPIRED",
                "This answer is no longer kept. Open the page again to ask again.");

        if (!row.Finished) await BackgroundRequests.EndStoppedAsync(db, [row]);

        // Work cancelled part way can have an answer too: what it did before it stopped.
        if (row.Answered)
        {
            Response.StatusCode = row.ResultStatus ?? StatusCodes.Status200OK;
            if (row.ResultContentType != null) Response.ContentType = row.ResultContentType;
            if (row.ResultDisposition != null) Response.Headers.ContentDisposition = row.ResultDisposition;
            if (row.ResultKey == null) await Response.Body.WriteAsync(row.Result ?? []);
            else
            {
                await using var kept = await cloudFiles.OpenReadAsync(row.ResultKey);
                await kept.CopyToAsync(Response.Body);
            }

            return new EmptyResult();
        }

        switch (row.Status)
        {            case BackgroundRequestStatus.Cancelled:
                return Refusal(StatusCodes.Status410Gone, "BACKGROUND_CANCELLED", "It was cancelled.");
            case BackgroundRequestStatus.TimedOut:
                return Refusal(StatusCodes.Status504GatewayTimeout, "BACKGROUND_TIMED_OUT",
                    $"It went {BackgroundRequests.Limit.TotalMinutes:n0} minutes without getting further and was stopped.");
            case BackgroundRequestStatus.Stopped:
                return Refusal(StatusCodes.Status503ServiceUnavailable, "BACKGROUND_STOPPED",
                    "The server running it stopped before it finished, as a deploy or restart does. Ask again.");
            default:
                return Refusal(StatusCodes.Status409Conflict, "BACKGROUND_RUNNING", "It is still running.");
        }
    }

    /// <summary>
    /// A link to the file one of the caller's answered with - an export's zip - for the browser to download
    /// the ordinary way, straight to disk. It opens for <see cref="BackgroundDownloads.Lasts"/>.
    /// </summary>
    [HttpPost("{id}/link")]
    public async Task<IActionResult> Link(string id)
    {
        var owner = Owner;
        if (owner == null) return Unauthorized();
        await requests.ForgetExpiredAsync(db);

        var row = await db.Set<BackgroundRequest>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && r.Owner == owner);
        if (row == null)
            return Refusal(StatusCodes.Status404NotFound, "BACKGROUND_EXPIRED", "This file is no longer kept. Export it again.");
        if (row.ResultKey == null || row.ResultStatus is not (>= 200 and < 300))
            return Refusal(StatusCodes.Status409Conflict, "BACKGROUND_NO_FILE", "It has no file to download.");
        return Ok(new { url = downloads.LinkTo(id) });
    }

    /// <summary>The file a link from <see cref="Link"/> opens. No sign-in: the link's seal is what lets it in.</summary>
    [AllowAnonymous]
    [HttpGet("{id}/file")]
    public async Task<IActionResult> Download(string id, [FromQuery] string expires, [FromQuery] string seal)
    {
        // The same answer for a bad seal as for a missing file, so a link can't be used to probe.
        if (!downloads.Opens(id, expires, seal)) return NotFound();
        var row = await db.Set<BackgroundRequest>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (row?.ResultKey == null) return NotFound();

        if (row.ResultDisposition != null) Response.Headers.ContentDisposition = row.ResultDisposition;
        return File(await cloudFiles.OpenReadAsync(row.ResultKey), row.ResultContentType ?? "application/octet-stream");
    }

    /// <summary>Stops one of the caller's, wherever it runs.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Cancel(string id)
    {
        var owner = Owner;
        if (owner == null) return Unauthorized();
        return await requests.CancelAsync(db, owner, id) ? NoContent() : NotFound();
    }

    string Owner => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    /// <summary>A refusal in the shape the UI reads one: the code, with what to tell the person.</summary>
    static IActionResult Refusal(int status, string code, string message) => new ContentResult
    {
        StatusCode = status,
        ContentType = "application/json",
        Content = System.Text.Encoding.UTF8.GetString(BackgroundRequests.Problem(code, message)),
    };
}
