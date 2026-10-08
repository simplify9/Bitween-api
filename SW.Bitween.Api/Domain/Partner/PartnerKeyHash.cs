using System;
using System.Security.Cryptography;
using System.Text;

namespace SW.Bitween.Domain
{
    /// <summary>
    /// Partner API keys are stored as a SHA-256 of the key, never the key itself, so a copy of the
    /// database no longer lets anyone call Bitween as every partner.
    /// </summary>
    /// <remarks>
    /// Unsalted on purpose. Keys are random — 128 bits from a GUID — so there is nothing to look up
    /// in a table, and an unsalted hash keeps the lookup a single indexed equality.
    /// </remarks>
    public static class PartnerKeyHash
    {
        public const string Prefix = "sha256:";

        public static string Of(string key) =>
            Prefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

        public static bool IsHashed(string stored) =>
            stored != null && stored.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>The stored form of a key, whichever form it arrives in.</summary>
        public static string Stored(string key) => IsHashed(key) ? key : Of(key);
    }
}
