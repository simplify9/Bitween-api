using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.RebexFtpUploadHandler;

public class RebexFtpUploadHandlerInput
{
    [Required]
    public string Host { get; set; } = string.Empty;

    public int? Port { get; set; }

    [Required]
    public string Username { get; set; } = string.Empty;

    [Secure]
    public string? Password { get; set; }

    public string? TargetPath { get; set; }

    public string? FileNamePrefix { get; set; }

    [DefaultValue("utf8")]
    public string DataEncoding { get; set; } = "utf8";

    [DefaultValue("sftp")]
    public string Protocol { get; set; } = "sftp";

    [Secure]
    public string? PrivateKey { get; set; }
}
