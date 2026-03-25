using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.HttpReceiver;

public class HttpReceiverInput
{
    public string? AuthType { get; set; }
    public string? ApiKey { get; set; }
    public string? LoginUrl { get; set; }
    public string? LoginUsername { get; set; }
    public string? LoginPassword { get; set; }

    [Required]
    public string Url { get; set; } = string.Empty;

    public string? Headers { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    [DefaultValue("application/json")]
    public string ContentType { get; set; } = "application/json";

    [DefaultValue("get")]
    public string Verb { get; set; } = "get";

    public string? DefaultRequest { get; set; }
    
    public string? ArrayPath { get; set; }
}