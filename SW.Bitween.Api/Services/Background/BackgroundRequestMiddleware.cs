using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace SW.Bitween.Services.Background;

/// <summary>
/// Lets work that takes long go on in the background instead of holding its caller: a GET or POST under
/// <c>/api</c> whose caller sent <c>Prefer: respond-async</c> is answered as usual when it answers within
/// the caller's <c>wait</c> (three seconds unless it says), and otherwise with <c>202 Accepted</c> and the
/// id of a background request that goes on running. The admin UI asks it only of the work that can
/// take long - prod's list to bring from, bringing, bulk retry, exporting files. Every other request
/// passes through untouched - the UI's other pages, the CLI, scripts, another Bitween - so they keep the
/// limits they always had.
/// </summary>
/// <remarks>
/// Sits after authentication and authorization, whose outcome the background request keeps, and before
/// the request context is filled in, which the background request does again for itself; see
/// <see cref="AskedRequest"/>.
/// </remarks>
public class BackgroundRequestMiddleware(RequestDelegate next, BackgroundRequests requests)
{
    /// <summary>What the UI calls the request, for the header. URL-encoded, being free text.</summary>
    public const string LabelHeader = "X-Bitween-Label";

    /// <summary>The UI address the request was made from. URL-encoded.</summary>
    public const string PageHeader = "X-Bitween-Page";

    static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(3);
    static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(30);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!Wanted(context, out var wait, out var owner))
        {
            await next(context);
            return;
        }

        var asked = await AskedRequest.OfAsync(context);
        if (asked == null)
        {
            await next(context);
            return;
        }

        var work = requests.Start(asked, next, owner, Header(context, LabelHeader), Header(context, PageHeader));

        BackgroundRequests.Answer answer = null;
        if (await Task.WhenAny(work.AnsweredHere.Task, Task.Delay(wait, context.RequestAborted)) == work.AnsweredHere.Task)
            answer = await work.AnsweredHere.Task;
        else if (context.RequestAborted.IsCancellationRequested)
        {
            // The caller left before it was answered, so nobody will ask for the answer.
            work.Cancel();
            return;
        }

        if (answer == null && !work.TryMove())
            // Answered between the wait running out and the move: answered as it came.
            answer = await work.AnsweredHere.Task;

        if (answer != null)
        {
            await using (answer.Body) await WriteAsync(context, answer);
            return;
        }

        var kept = await requests.KeepAsync(work);
        context.Response.StatusCode = StatusCodes.Status202Accepted;
        context.Response.Headers[HeaderNames.Location] = $"/api/background/{kept.Id}";
        context.Response.Headers["Preference-Applied"] = "respond-async";
        await context.Response.WriteAsJsonAsync(new { id = kept.Id, status = kept.Status.ToString() });
    }

    static bool Wanted(HttpContext context, out TimeSpan wait, out string owner)
    {
        wait = DefaultWait;
        owner = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsPost(context.Request.Method)) || owner == null ||
            !context.Request.Path.StartsWithSegments("/api") ||
            context.Request.Path.StartsWithSegments("/api/background"))
            return false;

        var asked = false;
        foreach (var preference in context.Request.Headers["Prefer"]
                     .SelectMany(v => (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            if (preference.Equals("respond-async", StringComparison.OrdinalIgnoreCase)) asked = true;
            else if (preference.StartsWith("wait=", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(preference["wait=".Length..], out var seconds) && seconds >= 0)
                wait = TimeSpan.FromSeconds(seconds) < LongestWait ? TimeSpan.FromSeconds(seconds) : LongestWait;
        }

        return asked;
    }

    static async Task WriteAsync(HttpContext context, BackgroundRequests.Answer answer)
    {
        switch (answer.Kind)
        {
            case BackgroundRequests.AnswerKind.Answered:
                context.Response.StatusCode = answer.Status;
                foreach (var header in answer.Headers)
                    if (!header.Key.Equals(HeaderNames.ContentLength, StringComparison.OrdinalIgnoreCase) &&
                        !header.Key.Equals(HeaderNames.TransferEncoding, StringComparison.OrdinalIgnoreCase))
                        context.Response.Headers[header.Key] = header.Value;
                await answer.Body.CopyToAsync(context.Response.Body);
                break;
            case BackgroundRequests.AnswerKind.TimedOut:
                // Not reachable within a wait of seconds; answered the way the database would have.
                context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
                break;
            default:
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                break;
        }
    }

    static string Header(HttpContext context, string name)
    {
        var value = context.Request.Headers[name].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }
}
