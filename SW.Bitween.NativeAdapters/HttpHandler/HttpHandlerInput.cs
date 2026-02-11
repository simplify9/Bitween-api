using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters;

public class HttpHandlerInput
{
    public string? AuthType { get; set; }
    public string? ApiKey { get; set; }
    public string? LoginUrl { get; set; }
    
    [DefaultValue("post")]
    public string Verb { get; set; } = "post";
    
    public string? LoginUsername { get; set; }
    public string? LoginPassword { get; set; }
    
    [Required]
    public string Url { get; set; } = string.Empty;
    
    [DefaultValue("application/json")]
    public string ContentType { get; set; } = "application/json";
    
    public string? Headers { get; set; }
    public string? CorrelationId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? DefaultRequest { get; set; }
}