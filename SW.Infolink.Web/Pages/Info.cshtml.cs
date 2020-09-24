using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SW.HttpExtensions;
using SW.PrimitiveTypes;

namespace SW.Infolink.Web.Pages
{
    public class Info : PageModel
    {
        private readonly JwtTokenParameters jwtTokenParameters;

        public Info(RequestContext requestContext, JwtTokenParameters jwtTokenParameters)
        {
            RequestContext = requestContext;
            this.jwtTokenParameters = jwtTokenParameters;
        }

        public string JwtValue { get; set; }

        public RequestContext RequestContext { get; }

        public void OnGet()
        {
            JwtValue = jwtTokenParameters.WriteJwt((ClaimsIdentity)RequestContext.User.Identity);
        }
    }
}
