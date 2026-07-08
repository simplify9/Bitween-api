using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.AzureBlobReceiver;

public class AzureBlobReceiverInput
{
    [Required]
    [Secure]
    public string ConnectionString { get; set; } = string.Empty;

    [Required]
    public string ContainerName { get; set; } = string.Empty;

    public string? FolderName { get; set; }

    [DefaultValue(50)]
    public int BatchSize { get; set; } = 50;

    [DefaultValue("utf8")]
    public string ResponseEncoding { get; set; } = "utf8";

    public string? DeleteMovesFileTo { get; set; }
}
