using System;

namespace SW.Bitween.Model
{
    /// <summary>A connection as the Connections page shows it. Secrets come back as <c>__private__</c> when set.</summary>
    public class ConnectionRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;

        /// <summary><c>ClientCredentials</c>, <c>AuthorizationCode</c> or <c>Password</c>.</summary>
        public string Grant { get; set; } = null!;

        public string? AuthorizeUrl { get; set; }
        public string? TokenUrl { get; set; }
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? Scopes { get; set; }
        public string? AuthorizeParameters { get; set; }
        public string? TokenAuth { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }

        /// <summary><c>New</c>, <c>Connected</c>, <c>NeedsAuthorizing</c> or <c>Error</c>.</summary>
        public string Status { get; set; } = null!;

        public string? LastError { get; set; }
        public DateTime? AccessTokenExpiresOn { get; set; }
        public bool HasRefreshToken { get; set; }
        public string? AuthorizedBy { get; set; }
        public DateTime? AuthorizedOn { get; set; }

        /// <summary>What a setting writes to use it, as <c>{{connections.NAME.accessToken}}</c>.</summary>
        public string Reference { get; set; } = null!;
    }

    /// <summary>
    /// What a connection is created or changed with. A secret sent as <c>__private__</c>, or not
    /// sent, keeps the one stored.
    /// </summary>
    public class ConnectionSave
    {
        public string Name { get; set; } = null!;
        public string Grant { get; set; } = null!;
        public string? AuthorizeUrl { get; set; }
        public string? TokenUrl { get; set; }
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? Scopes { get; set; }
        public string? AuthorizeParameters { get; set; }
        public string? TokenAuth { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
    }

    public class ConnectionAuthorize
    {
    }

    public class ConnectionAuthorizeResult
    {
        /// <summary>The provider's approval page to send the browser to.</summary>
        public string Url { get; set; } = null!;
    }

    /// <summary>What the provider sent the browser back to <c>/oauth-callback</c> with.</summary>
    public class ConnectionComplete
    {
        public string? Code { get; set; }
        public string? State { get; set; }
    }

    public class ConnectionTest
    {
    }

    public class ConnectionDelete
    {
    }
}
