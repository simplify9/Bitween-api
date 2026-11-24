using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters;

public class HttpHandlerInput
{
    [Description("Authentication type (e.g. ApiKey, Basic, OAuth2, Bearer).")]
    public string? AuthType { get; set; }
    [Secure]
    [Description("API key, sent as the ApiKey header with the ApiKey authentication type.")]
    public string? ApiKey { get; set; }
    [Description("URL used to obtain a login token for OAuth2 or Basic auth flows.")]
    public string? LoginUrl { get; set; }

    [DefaultValue("post")]
    [Description("HTTP verb to use when calling the endpoint (e.g. get, post, put, patch, delete).")]
    public string Verb { get; set; } = "post";

    [Description("Username for Basic and Login authentication.")]
    public string? LoginUsername { get; set; }
    [Secure]
    [Description("Password for Basic and Login authentication, and the token for Bearer: {{connections.NAME.accessToken}} works here.")]
    public string? LoginPassword { get; set; }
    [Description("Optional custom login request (Login auth type). Put the real username and password in LoginUsername and LoginPassword, then write {{username}} and {{password}} here where they belong, e.g. {\"email\":\"{{username}}\",\"password\":\"{{password}}\"}. This field is not hidden, so never type a secret into it directly. Leave empty to send the default body.")]
    public string? LoginBody { get; set; }
    [Description("Optional path to the token in the login response (e.g. token, data.access_token). Empty reads the 'jwt' field.")]
    public string? LoginTokenPath { get; set; }
    [Description("How the login request is sent (Login auth type): application/json (the default) or application/x-www-form-urlencoded, for token endpoints that take form fields. Either way LoginBody is written as a flat JSON object.")]
    public string? LoginContentType { get; set; }
    [Description("Where the login reply says how long the token lasts (Login auth type): seconds, or the date and time it expires (e.g. expires_in, data.expiresAt). Empty reads expires_in.")]
    public string? LoginExpiresInPath { get; set; }
    [Description("How long to reuse the token when the login reply doesn't say and the token isn't a JWT (Login auth type). Default 300. 0 signs in for every message, never reusing a token.")]
    public int? LoginTokenLifetimeSeconds { get; set; }
    [Description("Where the token goes on each request (Login auth type): 'Name: value' with {{token}} in the value, e.g. 'X-Auth-Token: {{token}}' or 'Authorization: Token {{token}}'. Empty sends 'Authorization: Bearer {{token}}'.")]
    public string? LoginTokenHeader { get; set; }

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
    [Description("Seconds to wait for the endpoint to answer before the delivery fails. Default 100.")]
    public int TimeoutSeconds { get; set; } = 100;
    [Description("OAuth2 client ID.")]
    public string? ClientId { get; set; }
    [Secure]
    [Description("OAuth2 client secret.")]
    public string? ClientSecret { get; set; }
    [Description("Default request body template used when the incoming document is empty.")]
    public string? DefaultRequest { get; set; }
}