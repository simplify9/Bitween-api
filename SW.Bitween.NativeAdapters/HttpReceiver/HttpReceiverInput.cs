using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.HttpReceiver;

public class HttpReceiverInput
{
    [Description("Authentication type used to call the source endpoint (e.g. ApiKey, Basic, OAuth2, Bearer).")]
    public string? AuthType { get; set; }
    [Secure]
    [Description("API key used for ApiKey or Bearer authentication.")]
    public string? ApiKey { get; set; }
    [Description("URL used to obtain a login token for OAuth2 or Basic auth flows.")]
    public string? LoginUrl { get; set; }
    [Description("Username for Basic or OAuth2 password-grant authentication.")]
    public string? LoginUsername { get; set; }
    [Secure]
    [Description("Password for Basic or OAuth2 password-grant authentication.")]
    public string? LoginPassword { get; set; }
    [Description("Optional custom login request (Login auth type). Put the real username and password in LoginUsername and LoginPassword, then write {{username}} and {{password}} here where they belong, e.g. {\"email\":\"{{username}}\",\"password\":\"{{password}}\"}. This field is not hidden, so never type a secret into it directly. Leave empty to send the default body.")]
    public string? LoginBody { get; set; }
    [Description("Optional path to the token in the login response (e.g. token, data.access_token). Empty reads the 'jwt' field.")]
    public string? LoginTokenPath { get; set; }

    [Required]
    [Description("The source HTTP endpoint URL to pull data from.")]
    public string Url { get; set; } = string.Empty;

    [Secure]
    [Description("Additional HTTP headers as key=value pairs separated by newlines.")]
    public string? Headers { get; set; }
    [Description("OAuth2 client ID.")]
    public string? ClientId { get; set; }
    [Secure]
    [Description("OAuth2 client secret.")]
    public string? ClientSecret { get; set; }

    [DefaultValue("application/json")]
    [Description("Content-Type header sent with the request (e.g. application/json, application/xml).")]
    public string ContentType { get; set; } = "application/json";

    [DefaultValue("get")]
    [Description("HTTP verb to use when pulling data (e.g. get, post).")]
    public string Verb { get; set; } = "get";

    [Description("Default request body template used when no payload is needed.")]
    public string? DefaultRequest { get; set; }

    [Description("JSON path to the array element in the response to iterate over (e.g. $.items).")]
    public string? ArrayPath { get; set; }

    [Description("Seconds to wait for the endpoint to answer before the run fails. Default 100.")]
    public int TimeoutSeconds { get; set; } = 100;

    [Description("For an API that pages its results: the path to the next page's address in each reply (e.g. next, links.next, @odata.nextLink). Pages are followed until it is empty or missing, on the same server only.")]
    public string? NextLinkPath { get; set; }

    [Description("For an API that numbers its pages: the query parameter that does it (e.g. page). Pages are asked for from PageStart up, until one has no items.")]
    public string? PageParameter { get; set; }

    [DefaultValue(1)]
    [Description("The first page's number, with PageParameter. Default 1.")]
    public int PageStart { get; set; } = 1;

    [DefaultValue(20)]
    [Description("The most pages one run reads, with NextLinkPath or PageParameter. Default 20.")]
    public int MaxPages { get; set; } = 20;
}
