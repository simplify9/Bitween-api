using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.RebexFtpReceiver;

public class RebexFtpReceiverInput
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

    [Description("Remote directory to change into before listing/downloading files.")]
    public string? TargetPath { get; set; }

    [DefaultValue(50)]
    [Description("Maximum number of files to fetch per polling batch.")]
    public int BatchSize { get; set; } = 50;

    [DefaultValue("utf8")]
    [Description("Encoding used to decode file contents (e.g. utf8).")]
    public string ResponseEncoding { get; set; } = "utf8";

    [Description("Folder to move a file to after it's received. If empty, the file is deleted instead.")]
    public string? DeleteMovesFileTo { get; set; }

    [DefaultValue("sftp")]
    [Description("Transfer protocol to use (sftp or ftp).")]
    public string Protocol { get; set; } = "sftp";

    [DefaultValue(true)]
    [Description("Whether to verify the file still exists before attempting to delete or move it after receiving.")]
    public bool CheckFileExistence { get; set; } = true;

    [Secure]
    [Description("Private key used for SFTP authentication instead of a password.")]
    public string? PrivateKey { get; set; }
}
