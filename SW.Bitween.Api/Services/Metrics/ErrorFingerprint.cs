using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SW.Bitween.Services.Metrics;

/// <summary>
/// Groups errors that are the same mistake with different details: the first line of the message,
/// with ids, numbers, dates, addresses and quoted values replaced by a mark, hashed.
/// </summary>
public static partial class ErrorFingerprint
{
    public const int SampleLength = 500;

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex Guid();
    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?Z?")]
    private static partial Regex Timestamp();
    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex Url();
    [GeneratedRegex(@"""[^""]*""|'[^']*'")]
    private static partial Regex Quoted();
    [GeneratedRegex(@"\b[0-9a-fA-F]{12,}\b")]
    private static partial Regex Hex();
    [GeneratedRegex(@"\d+")]
    private static partial Regex Number();

    /// <summary>The line worth showing: the first non-empty one, cut to fit.</summary>
    public static string Sample(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "(no message)";
        foreach (var line in message.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0) return trimmed.Length <= SampleLength ? trimmed : trimmed[..SampleLength];
        }
        return "(no message)";
    }

    public static string Of(string message)
    {
        var shape = Sample(message);
        shape = Guid().Replace(shape, "#");
        shape = Timestamp().Replace(shape, "#");
        shape = Url().Replace(shape, "#");
        shape = Quoted().Replace(shape, "#");
        shape = Hex().Replace(shape, "#");
        shape = Number().Replace(shape, "#");
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(shape));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}
