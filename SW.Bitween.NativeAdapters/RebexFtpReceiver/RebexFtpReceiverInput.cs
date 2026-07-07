using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.RebexFtpReceiver;

public class RebexFtpReceiverInput
{
    [Required]
    public string Host { get; set; } = string.Empty;

    public int? Port { get; set; }

    [Required]
    public string Username { get; set; } = string.Empty;

    [Secure]
    public string? Password { get; set; }

    public string? TargetPath { get; set; }

    [DefaultValue(50)]
    public int BatchSize { get; set; } = 50;

    [DefaultValue("utf8")]
    public string ResponseEncoding { get; set; } = "utf8";

    public string? DeleteMovesFileTo { get; set; }

    [DefaultValue("sftp")]
    public string Protocol { get; set; } = "sftp";

    [DefaultValue(true)]
    public bool CheckFileExistence { get; set; } = true;

    [Secure]
    public string? PrivateKey { get; set; }
}
