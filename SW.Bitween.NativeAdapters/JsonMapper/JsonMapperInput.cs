using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters.JsonMapper;

public class JsonMapperInput
{
    [Required]
    public string ScribanTemplate { get; set; } = "{}";
}
