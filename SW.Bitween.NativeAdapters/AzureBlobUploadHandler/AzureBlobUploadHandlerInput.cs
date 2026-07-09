using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.AzureBlobUploadHandler;

public class AzureBlobUploadHandlerInput
{
    [Required]
    [Secure]
    public string ConnectionString { get; set; } = string.Empty;

    [Required]
    public string ContainerName { get; set; } = string.Empty;

    public string? FileName { get; set; }

    public string? FileExtension { get; set; }
}
