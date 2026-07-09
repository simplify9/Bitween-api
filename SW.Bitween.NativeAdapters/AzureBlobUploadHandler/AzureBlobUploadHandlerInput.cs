using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.AzureBlobUploadHandler;

public class AzureBlobUploadHandlerInput
{
    [Required]
    [Secure]
    [Description("Azure Storage account connection string.")]
    public string ConnectionString { get; set; } = string.Empty;

    [Required]
    [Description("Name of the blob container to upload files to.")]
    public string ContainerName { get; set; } = string.Empty;

    [Description("Name to give the uploaded blob. If empty, a name is generated from the current timestamp and a GUID.")]
    public string? FileName { get; set; }

    [Description("Extension appended to the generated blob name (e.g. json, csv). Ignored if FileName is set.")]
    public string? FileExtension { get; set; }
}
