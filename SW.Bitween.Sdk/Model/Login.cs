using System;

namespace SW.Bitween.Model
{
    public class UserLogin
    {
        /// <summary>Null on a refresh-token or Microsoft-token sign-in.</summary>
        public string? Username { get; set; }

        public string? Password { get; set; }

        /// <summary>Sent instead of a password to renew an expired session.</summary>
        public string? RefreshToken { get; set; }

        /// <summary>Sent instead of a password when Microsoft sign-in is on.</summary>
        public string? MsToken { get; set; }

        /// <summary>
        /// Sent instead of a password with <see cref="OidcState"/>: the code an OpenID Connect
        /// provider sent the browser back with.
        /// </summary>
        public string? OidcCode { get; set; }

        public string? OidcState { get; set; }
    }

    /// <summary>Nothing to send: what the sign-in needs is in Bitween's settings.</summary>
    public class OidcStartRequest
    {
    }

    public class OidcStartResult
    {
        /// <summary>The provider's sign-in address to send the browser to.</summary>
        public string Url { get; set; } = null!;
    }


    public class AccountLoginResult
    {
        public string Jwt { get; set; } = null!;
        public string RefreshToken { get; set; } = null!;
    }

 
}