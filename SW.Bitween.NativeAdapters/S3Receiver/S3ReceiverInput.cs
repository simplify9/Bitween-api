using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.S3Receiver;

public class S3ReceiverInput
{
    [Required]
    [Description("S3-compatible access key ID.")]
    public string AccessKeyId { get; set; } = string.Empty;

    [Required]
    [Secure]
    [Description("S3-compatible secret access key.")]
    public string SecretAccessKey { get; set; } = string.Empty;

    [Required]
    [Description("S3-compatible service endpoint URL.")]
    public string ServiceUrl { get; set; } = string.Empty;

    [Required]
    [Description("Name of the bucket to read files from.")]
    public string BucketName { get; set; } = string.Empty;

    [Description("Folder (object key prefix) within the bucket to read files from.")]
    public string? FolderName { get; set; }

    [DefaultValue(50)]
    [Description("Maximum number of files to fetch per polling batch.")]
    public int BatchSize { get; set; } = 50;

    [DefaultValue("utf8")]
    [Description("Encoding used to decode file contents (e.g. utf8).")]
    public string ResponseEncoding { get; set; } = "utf8";

    [Description("Folder to move a file to after it's received. If empty, the file is deleted instead.")]
    public string? DeleteMovesFileTo { get; set; }
}
