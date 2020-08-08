using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SW.Infolink;

namespace SW.Infolink.Web.Pages
{
    public class LoginModel : PageModel
    {
        private readonly InfolinkSettings infolinkSettings;

        public LoginModel(InfolinkSettings infolinkSettings)
        {
            this.infolinkSettings = infolinkSettings;
        }

        public void OnGet()
        {
        }

        [Required]
        [BindProperty]
        public string Username { get; set; }

        [Required]
        [BindProperty]
        public string Password { get; set; }

        async public Task OnPostAsync()
        {

            if (!ModelState.IsValid) return;  

            var cred = infolinkSettings.AdminCredentials.Split(":");

            if (cred[0].Equals(Username, StringComparison.OrdinalIgnoreCase))
            {
                if (cred[1].Equals(Password))
                {
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, cred[0]),
                        //new Claim("FullName", "blabla"),
                        //new Claim(ClaimTypes.Role, "Supervisor"),
                    };

                    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                    var authProperties = new AuthenticationProperties
                    {
                        //AllowRefresh = <bool>,
                        // Refreshing the authentication session should be allowed.

                        //ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10),
                        // The time at which the authentication ticket expires. A 
                        // value set here overrides the ExpireTimeSpan option of 
                        // CookieAuthenticationOptions set with AddCookie.

                        //IsPersistent = true,
                        // Whether the authentication session is persisted across 
                        // multiple requests. When used with cookies, controls
                        // whether the cookie's lifetime is absolute (matching the
                        // lifetime of the authentication ticket) or session-based.

                        //IssuedUtc = <DateTimeOffset>,
                        // The time at which the authentication ticket was issued.

                        //RedirectUri = <string>
                        // The full path or absolute URI to be used as an http 
                        // redirect response value.
                    };

                    await HttpContext.SignInAsync(
                        scheme: CookieAuthenticationDefaults.AuthenticationScheme,
                        principal: new ClaimsPrincipal(claimsIdentity),
                        properties: authProperties);
                }
            }

            ModelState.AddModelError("LoginFailure", "Incorrect username or password. please try again.");

        }
    }
}
