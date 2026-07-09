using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.RebexFtpUploadHandler;

public class RebexFtpUploadHandlerInput
{
    [Required]
    [Description("FTP/SFTP server hostname.")]
    public string Host { get; set; } = string.Empty;

    [Description("Server port. If empty, the protocol's default port is used.")]
    public int? Port { get; set; }

    [Required]
    [Description("FTP/SFTP account username.")]
    public string Username { get; set; } = string.Empty;

    [Secure]
    [Description("Account password, or the private key passphrase when using SFTP with a private key.")]
    public string? Password { get; set; }

    [Description("Remote directory to upload the file into.")]
    public string? TargetPath { get; set; }

    [Description("Prefix added to the uploaded file's name.")]
    public string? FileNamePrefix { get; set; }

    [DefaultValue("utf8")]
    [Description("Encoding used to write the file contents (e.g. utf8).")]
    public string DataEncoding { get; set; } = "utf8";

    [DefaultValue("sftp")]
    [Description("Transfer protocol to use (sftp or ftp).")]
    public string Protocol { get; set; } = "sftp";

    [Secure]
    [Description("Private key used for SFTP authentication instead of a password.")]
    public string? PrivateKey { get; set; }
}
