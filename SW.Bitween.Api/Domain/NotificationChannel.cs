using System;
using System.Collections.Generic;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;

/// <summary>
/// Somewhere Bitween can send a notification: a handler adapter and everything it needs to deliver,
/// such as the recipients or a webhook url. Set up once in Settings, then picked by name wherever
/// something is worth telling someone about.
/// </summary>
/// <remarks>
/// A channel is complete. The places that use one choose only <em>when</em> to send, never how, so
/// what the Settings page shows for a channel is exactly what every use of it sends. Different
/// recipients are a second channel.
/// </remarks>
public class NotificationChannel : BaseEntity, IAudited
{
    public string Name { get; set; }

    /// <summary>Any handler adapter, built in or custom.</summary>
    public string HandlerId { get; set; }

    /// <summary>The adapter's settings. Encrypted at rest, since they can hold passwords and keys.</summary>
    public IReadOnlyDictionary<string, string> HandlerProperties { get; set; } =
        new Dictionary<string, string>();

    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string ModifiedBy { get; set; }
}
