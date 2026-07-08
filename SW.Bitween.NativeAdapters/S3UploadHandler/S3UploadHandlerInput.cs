using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.S3UploadHandler;

public class S3UploadHandlerInput
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

    public string? FileName { get; set; }

    public string? FileExtension { get; set; }

    [DefaultValue("text/plain")]
    public string ContentType { get; set; } = "text/plain";
}
