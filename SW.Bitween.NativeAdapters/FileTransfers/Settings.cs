using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.FileTransfers;

/// <summary>What every pickup from a file server is told, whichever protocol reaches it.</summary>
public interface IPickupSettings
{
    string? TargetPath { get; }
    int BatchSize { get; }
    string ResponseEncoding { get; }
    string? DeleteMovesFileTo { get; }
    bool CheckFileExistence { get; }
    int MinimumFileAgeSeconds { get; }
}

/// <summary>What every upload to a file server is told, whichever protocol reaches it.</summary>
public interface IUploadSettings
{
    string? TargetPath { get; }
    string? FileNamePrefix { get; }
    string DataEncoding { get; }
}

/// <summary>Reaching an SFTP server: the names and meanings the Rebex FTP adapters use for SFTP.</summary>
public abstract class SftpConnectionSettings
{
    [Required]
    [Description("SFTP server hostname.")]
    public string Host { get; set; } = string.Empty;

    [Description("Server port. If empty, 22 is used.")]
    public int? Port { get; set; }

    [Required]
    [Description("SFTP account username.")]
    public string Username { get; set; } = string.Empty;

    [Secure]
    [Description("Account password, or the private key's passphrase when a private key is set.")]
    public string? Password { get; set; }

    [Secure]
    [Description("Private key to sign in with instead of a password, in PEM or OpenSSH format. A PEM key pasted onto one line is re-wrapped.")]
    public string? PrivateKey { get; set; }

    [Description("SHA-256 fingerprint of the server's host key, as 'ssh-keygen -lf' prints it (SHA256:...). When set, a server presenting any other key is refused before credentials are sent.")]
    public string? HostKeyFingerprint { get; set; }
}

/// <summary>Reaching an FTP or FTPS server.</summary>
public abstract class FtpConnectionSettings
{
    [Required]
    [Description("FTP server hostname.")]
    public string Host { get; set; } = string.Empty;

    [Description("Server port. If empty, 990 is used for implicit FTPS and 21 otherwise.")]
    public int? Port { get; set; }

    [Required]
    [Description("FTP account username.")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [Secure]
    [Description("FTP account password.")]
    public string? Password { get; set; }

    [DefaultValue("none")]
    [Description("none for plain FTP, explicit for FTPS that upgrades the connection with AUTH TLS (usually port 21), or implicit for FTPS that is TLS from the start (usually port 990).")]
    public string Encryption { get; set; } = "none";

    [Description("For FTPS: the SHA-256 thumbprint of the server's certificate, in hex. When set, exactly that certificate is trusted, a self-signed one included, and any other is refused before credentials are sent. When empty, the certificate must be valid.")]
    public string? CertificateThumbprint { get; set; }

    [DefaultValue(true)]
    [Description("Passive mode, where the client opens the data connections; what almost every server behind a firewall needs. Turn off for active mode.")]
    public bool PassiveMode { get; set; } = true;
}

public class SftpPickupSettings : SftpConnectionSettings, IPickupSettings
{
    [Description("Remote directory to read files from.")]
    public string? TargetPath { get; set; }

    [DefaultValue(50)]
    [Description("Maximum number of files to fetch per polling batch.")]
    public int BatchSize { get; set; } = 50;

    [DefaultValue("utf8")]
    [Description("Encoding used to decode file contents: utf8, or base64 for binary files.")]
    public string ResponseEncoding { get; set; } = "utf8";

    [Description("Folder to move a file to after it's received. If empty, the file is deleted instead.")]
    public string? DeleteMovesFileTo { get; set; }

    [DefaultValue(true)]
    [Description("Whether to verify the file still exists before attempting to delete or move it after receiving.")]
    public bool CheckFileExistence { get; set; } = true;

    [Description("Seconds a file must have gone unchanged before it is taken, so one still being uploaded is left for the next run. 0 takes every file listed.")]
    public int MinimumFileAgeSeconds { get; set; }
}

public class FtpPickupSettings : FtpConnectionSettings, IPickupSettings
{
    [Description("Remote directory to read files from.")]
    public string? TargetPath { get; set; }

    [DefaultValue(50)]
    [Description("Maximum number of files to fetch per polling batch.")]
    public int BatchSize { get; set; } = 50;

    [DefaultValue("utf8")]
    [Description("Encoding used to decode file contents: utf8, or base64 for binary files.")]
    public string ResponseEncoding { get; set; } = "utf8";

    [Description("Folder to move a file to after it's received. If empty, the file is deleted instead.")]
    public string? DeleteMovesFileTo { get; set; }

    [DefaultValue(true)]
    [Description("Whether to verify the file still exists before attempting to delete or move it after receiving.")]
    public bool CheckFileExistence { get; set; } = true;

    [Description("Seconds a file must have gone unchanged before it is taken, so one still being uploaded is left for the next run. 0 takes every file listed.")]
    public int MinimumFileAgeSeconds { get; set; }
}

public class SftpUploadSettings : SftpConnectionSettings, IUploadSettings
{
    [Description("Remote directory to upload the file into.")]
    public string? TargetPath { get; set; }

    [Description("Prefix added to the uploaded file's name.")]
    public string? FileNamePrefix { get; set; }

    [DefaultValue("utf8")]
    [Description("Encoding used to write the file contents: utf8, or base64 to write the decoded bytes.")]
    public string DataEncoding { get; set; } = "utf8";
}

public class FtpUploadSettings : FtpConnectionSettings, IUploadSettings
{
    [Description("Remote directory to upload the file into.")]
    public string? TargetPath { get; set; }

    [Description("Prefix added to the uploaded file's name.")]
    public string? FileNamePrefix { get; set; }

    [DefaultValue("utf8")]
    [Description("Encoding used to write the file contents: utf8, or base64 to write the decoded bytes.")]
    public string DataEncoding { get; set; } = "utf8";
}
