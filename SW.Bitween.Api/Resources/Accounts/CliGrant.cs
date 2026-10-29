using System;
using System.Threading.Tasks;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

public class CliGrantRequest
{
    /// <summary>The CLI's S256 code challenge.</summary>
    public string CodeChallenge { get; set; }
}

/// <summary>
/// A signed-in member signing the bitween CLI in as themselves, from the admin UI's confirmation
/// page: the code it returns goes back to the CLI, which redeems it at <see cref="CliToken"/>. It
/// works whichever way the member signed in to the browser, Microsoft included. An account that
/// must change its password never gets here; the pipeline refuses its token everywhere else.
/// </summary>
[HandlerName("cligrant")]
public class CliGrant(RequestContext requestContext, CliSignInCodes codes) : ICommandHandler<CliGrantRequest, object>
{
    public Task<object> Handle(CliGrantRequest request)
    {
        if (!CliSignInCodes.IsChallenge(request?.CodeChallenge))
            throw new SWValidationException("CodeChallenge", "This isn't a sign-in request from the bitween CLI.");

        var accountId = Convert.ToInt32(requestContext.GetNameIdentifier());
        return Task.FromResult<object>(new { Code = codes.Issue(accountId, request.CodeChallenge, DateTimeOffset.UtcNow) });
    }
}
