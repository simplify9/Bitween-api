using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.S3Receiver;

public class S3ReceiverInput
{
    [Required]
    public string AccessKeyId { get; set; } = string.Empty;

    [Required]
    [Secure]
    public string SecretAccessKey { get; set; } = string.Empty;

    [Required]
    public string ServiceUrl { get; set; } = string.Empty;

    [Required]
    public string BucketName { get; set; } = string.Empty;

    public string? FolderName { get; set; }

    [DefaultValue(50)]
    public int BatchSize { get; set; } = 50;

    [DefaultValue("utf8")]
    public string ResponseEncoding { get; set; } = "utf8";

    public string? DeleteMovesFileTo { get; set; }
}
