using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.S3UploadHandler;

public class S3UploadHandlerInput
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
    [Description("Name of the bucket to upload files to.")]
    public string BucketName { get; set; } = string.Empty;

    [Description("Folder (object key prefix) within the bucket to upload files to.")]
    public string? FolderName { get; set; }

    [Description("Name to give the uploaded object. If empty, a name is generated from the current timestamp and a GUID.")]
    public string? FileName { get; set; }

    [Description("Extension appended to the generated object key (e.g. json, csv). Ignored if FileName is set.")]
    public string? FileExtension { get; set; }

    [DefaultValue("text/plain")]
    [Description("Content-Type to set on the uploaded object (e.g. text/plain, application/json).")]
    public string ContentType { get; set; } = "text/plain";
}
