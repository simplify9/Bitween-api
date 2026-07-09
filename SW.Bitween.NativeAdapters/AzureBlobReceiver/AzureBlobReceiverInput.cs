using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.AzureBlobReceiver;

public class AzureBlobReceiverInput
{
    [Required]
    [Secure]
    [Description("Azure Storage account connection string.")]
    public string ConnectionString { get; set; } = string.Empty;

    [Required]
    [Description("Name of the blob container to read files from.")]
    public string ContainerName { get; set; } = string.Empty;

    [Description("Folder (blob name prefix) within the container to read files from.")]
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
