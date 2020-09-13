using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SW.HttpExtensions;
using SW.PrimitiveTypes;

namespace SW.Infolink.Web.Pages
{
    public class Jwt : PageModel
    {
        private readonly RequestContext requestContext;
        private readonly JwtTokenParameters jwtTokenParameters;

        public Jwt(RequestContext requestContext, JwtTokenParameters jwtTokenParameters)
        {
            this.requestContext = requestContext;
            this.jwtTokenParameters = jwtTokenParameters;
        }

        [BindProperty]
        public string JwtValue { get; set; }

        public void OnGet()
        {
            JwtValue = jwtTokenParameters.WriteJwt((ClaimsIdentity)requestContext.User.Identity);
        }
    }
}
