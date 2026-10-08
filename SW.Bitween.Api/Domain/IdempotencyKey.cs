using System;

namespace SW.Bitween.Domain;

/// <summary>
/// A partner's Idempotency-Key, and the exchange the first call carrying it created. A partner
/// whose call timed out can't tell whether it arrived; sending it again with the same key returns
/// that exchange instead of making a second one.
/// </summary>
public class IdempotencyKey
{
    private IdempotencyKey()
    {
    }

    public IdempotencyKey(string scope, string key, string xchangeId)
    {
        Scope = scope;
        Key = key;
        XchangeId = xchangeId;
        CreatedOn = DateTime.UtcNow;
    }

    public long Id { get; private set; }

    /// <summary>Who the key belongs to — gateway and partner — so two callers can't collide.</summary>
    public string Scope { get; private set; }

    public string Key { get; private set; }
    public string XchangeId { get; private set; }
    public DateTime CreatedOn { get; private set; }
}
