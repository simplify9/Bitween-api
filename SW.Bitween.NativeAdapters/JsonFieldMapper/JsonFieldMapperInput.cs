using System.ComponentModel.DataAnnotations;

namespace SW.Bitween.NativeAdapters;

public class JsonFieldMapperInput
{
    [Required]
    public string Rules { get; set; } = "[]";
}
