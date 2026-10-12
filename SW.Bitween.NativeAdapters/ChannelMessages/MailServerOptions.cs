namespace SW.Bitween.NativeAdapters.ChannelMessages;

/// <summary>
/// The one mail server every Email channel sends through, set once in Settings so that a channel
/// only has to say who receives it.
/// </summary>
/// <remarks>
/// Read per send, never copied: Settings changes these values on the running instance, and the
/// next email goes out with them.
/// </remarks>
public class MailServerOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool UseTls { get; set; } = true;

    /// <summary>Trust exactly this certificate, as the SMTP adapter's setting of the same name does.</summary>
    public string? CertificateThumbprint { get; set; }

    public string? From { get; set; }
    public string? FromName { get; set; }

    /// <summary>Whether there is enough here to send anything at all.</summary>
    public bool IsSet => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);
}
