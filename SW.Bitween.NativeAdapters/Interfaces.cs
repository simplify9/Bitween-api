using SW.PrimitiveTypes;

namespace SW.Bitween.NativeAdapters;

public interface INativeAdapter
{
    public string Name { get; }
    public void InitializeStartupValues(IDictionary<string, string> settings);
    public Type StartupValuesType { get; }
}

/// <summary>
/// Marks an adapter that only works with a Rebex license key configured. Such adapters are
/// always registered - the key is a setting that can change at runtime - but they're kept out
/// of the adapter pickers while no key is set.
/// </summary>
public interface IRequiresRebexLicense { }

/// <summary>
/// A receiver that keeps a bookmark through Bitween: where its last good run got to, as the
/// <c>bitween-bookmark</c> contract describes for packaged receivers.
/// </summary>
/// <remarks>
/// Bitween puts the bookmark the subscription's last good run left in the receiver's settings as
/// <c>__bookmark__</c> (absent on a first run), and calls <see cref="Bookmark"/> once every item of a
/// run was taken in, keeping what it returns for the next run. A run in which anything failed
/// doesn't move it.
/// </remarks>
public interface IKeepsBookmark
{
    /// <summary>Where the next run starts; null or empty keeps the bookmark as it was.</summary>
    Task<string?> Bookmark();
}

/// <summary>
/// Marks a mapper that is given the partner and global values as context of its own, rather than
/// reading them out of the payload.
/// </summary>
/// <remarks>
/// <para>
/// Partner and global values reach a mapper by being written <em>into</em> the incoming document as
/// <c>__partner__</c> and <c>__globals__</c> keys (see <c>XchangeService.RunMapper</c>). That costs
/// more than it looks: the payload has to be parsed as JSON before anything knows which mapper is
/// configured, so a non-JSON document cannot get that far; any document with a real key of the same
/// name loses it; and only a JSON object is enriched, so the same mapping behaves differently for a
/// root array.
/// </para>
/// <para>
/// A mapper marked this way is handed the values separately and the payload is left exactly as it
/// arrived - which is the only way a format other than JSON can be mapped at all.
/// </para>
/// </remarks>
public interface IReceivesMappingContext { }

/// <summary>
/// Marks a handler that delivers Bitween's own notification messages, the Email, Teams and Slack
/// channel types, rather than a document. It is chosen by picking that type for a notification
/// channel, so it is kept out of every adapter picker, where it could only ever fail.
/// </summary>
public interface IChannelMessageAdapter { }

public interface INativeInfolinkHandler : INativeAdapter, IInfolinkHandler { }
public interface INativeInfolinkMapper : INativeAdapter, IInfolinkHandler { }
public interface INativeInfolinkValidator : IInfolinkValidator, INativeAdapter { }
public interface INativeInfolinkReceiver : IInfolinkReceiver, INativeAdapter { }
