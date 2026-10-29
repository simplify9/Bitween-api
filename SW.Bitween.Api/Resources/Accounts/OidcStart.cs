using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SW.Bitween.Model;
using SW.Bitween.Services;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

/// <summary>Starts a single sign-on: the provider's address to send the browser to.</summary>
[HandlerName("oidcstart")]
[Unprotect]
public class OidcStart(OidcSignIn oidc, ILogger<OidcStart> logger) : ICommandHandler<OidcStartRequest, OidcStartResult>
{
    public async Task<OidcStartResult> Handle(OidcStartRequest request)
    {
        try
        {
            return new OidcStartResult { Url = await oidc.StartAsync() };
        }
        catch (OidcSignIn.Refused ex)
        {
            throw new SWException(ex.Message);
        }
        catch (System.Exception ex)
        {
            logger.LogWarning(ex, "Single sign-on couldn't start: the provider's metadata couldn't be read.");
            throw new SWException("Single sign-on couldn't reach the provider. Please try again, or contact your administrator.");
        }
    }
}
