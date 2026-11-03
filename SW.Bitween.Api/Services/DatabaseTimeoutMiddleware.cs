using System;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SW.Bitween.Services.Background;

namespace SW.Bitween.Services;

/// <summary>
/// A request whose database command ran past its time limit, answered as that: 504 with
/// <c>DATABASE_TIMED_OUT</c>, where it used to be a bare 500 like any other failure. The admin UI
/// doesn't ask again for it - asked again, it takes as long again - and says what happened rather
/// than showing an empty page.
/// </summary>
public class DatabaseTimeoutMiddleware(RequestDelegate next, ILogger<DatabaseTimeoutMiddleware> logger)
{
    public const string Code = "DATABASE_TIMED_OUT";

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (IsDatabaseTimeout(ex) && !context.Response.HasStarted)
        {
            logger.LogError(ex, "{Method} {Path} ran past the database's time limit.", context.Request.Method,
                context.Request.Path);
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync(BackgroundRequests.Problem(Code,
                "The database took too long to answer. Asking for less, such as a shorter time range, may help."));
        }
    }

    /// <summary>
    /// A command that ran out of time, as Npgsql throws it: its own exception around a
    /// <see cref="TimeoutException"/>. Only inside a database exception, so a call elsewhere that
    /// timed out isn't put down to the database. SQL Server and MySQL say it differently, and keep
    /// the 500.
    /// </summary>
    internal static bool IsDatabaseTimeout(Exception ex)
    {
        var inDatabase = false;
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is DbException)
                inDatabase = true;
            else if (e is TimeoutException && inDatabase)
                return true;
        }

        return false;
    }
}
