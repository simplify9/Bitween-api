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
}