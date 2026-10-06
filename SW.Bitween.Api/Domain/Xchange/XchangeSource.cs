using System.Collections.Generic;

namespace SW.Bitween.Domain
{
    /// <summary>
    /// Where an exchange came from, when it came from a delivery's response: the exchange that made
    /// the delivery, and what this one reads from that exchange's input, the original document.
    /// </summary>
    /// <param name="XchangeId">The exchange whose delivery's response this one runs on.</param>
    /// <param name="Values">
    /// Values read from the original document, by path. Null until they are read — a message off the
    /// bus carries only the id, and each route reads what it uses.
    /// </param>
    public record XchangeSource(string XchangeId, IReadOnlyDictionary<string, string> Values = null);
}
