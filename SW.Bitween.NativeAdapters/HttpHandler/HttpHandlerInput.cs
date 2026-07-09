using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters;

public class HttpHandlerInput
{
    [Description("Authentication type (e.g. ApiKey, Basic, OAuth2, Bearer).")]
    public string? AuthType { get; set; }
    [Secure]
    [Description("API key used for ApiKey or Bearer authentication.")]
    public string? ApiKey { get; set; }
    [Description("URL used to obtain a login token for OAuth2 or Basic auth flows.")]
    public string? LoginUrl { get; set; }

    [DefaultValue("post")]
    [Description("HTTP verb to use when calling the endpoint (e.g. get, post, put, patch, delete).")]
    public string Verb { get; set; } = "post";

    [Description("Username for Basic or OAuth2 password-grant authentication.")]
    public string? LoginUsername { get; set; }
    [Secure]
    [Description("Password for Basic or OAuth2 password-grant authentication.")]
    public string? LoginPassword { get; set; }

    [Required]
    [Description("The target HTTP endpoint URL.")]
    public string Url { get; set; } = string.Empty;

    [DefaultValue("application/json")]
    [Description("Content-Type header sent with the request (e.g. application/json, application/xml).")]
    public string ContentType { get; set; } = "application/json";

    [Secure]
    [Description("Additional HTTP headers as key=value pairs separated by newlines.")]
    public string? Headers { get; set; }
    [Description("Field name or JSON path used to extract a correlation ID from the response.")]
    public string? CorrelationId { get; set; }
    [Description("OAuth2 client ID.")]
    public string? ClientId { get; set; }
    [Secure]
    [Description("OAuth2 client secret.")]
    public string? ClientSecret { get; set; }
    [Description("Default request body template used when the incoming document is empty.")]
    public string? DefaultRequest { get; set; }
}