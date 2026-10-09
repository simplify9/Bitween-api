using System;
using System.Security.Cryptography;
using System.Text;

namespace SW.Bitween.Adapters;

/// <summary>
/// A file as Bitween passes it to and from adapters: the content, its name, and whether it is a
/// bad response. On the wire it is the JSON <c>{"Filename","Data","Hash","BadData","ContentType"}</c>
/// that <c>SW.PrimitiveTypes.XchangeFile</c> has always produced, so adapters built on either
/// exchange the same thing.
/// </summary>
public class ExchangeFile
{
    private string _data = string.Empty;

    /// <summary>For deserializers; <see cref="Data"/> is empty until set.</summary>
    public ExchangeFile()
    {
    }

    /// <param name="data">The content: text, or base64 for binary content.</param>
    /// <param name="filename">The file's name, when it has one.</param>
    /// <param name="badData">Whether this is a bad response — a delivery the partner rejected.</param>
    public ExchangeFile(string data, string? filename = null, bool badData = false)
    {
        Data = data;
        Filename = filename;
        BadData = badData;
    }

    /// <summary>The file's name, when it has one.</summary>
    public string? Filename { get; set; }

    /// <summary>The content: text, or base64 for binary content.</summary>
    public string Data
    {
        get => _data;
        set => _data = value ?? throw new ArgumentNullException(nameof(Data), "A file's data can't be null.");
    }

    /// <summary>SHA-1 of <see cref="Data"/> as lower-case hex. Always derived from the data; a value read from JSON is ignored.</summary>
    public string Hash
    {
        get
        {
            using var sha1 = SHA1.Create();
            return BitConverter.ToString(sha1.ComputeHash(Encoding.UTF8.GetBytes(Data))).Replace("-", "").ToLowerInvariant();
        }
    }

    /// <summary>Whether this is a bad response — a delivery the partner rejected.</summary>
    public bool BadData { get; set; }

    /// <summary>The content's media type, when known.</summary>
    public string? ContentType { get; set; }
}
