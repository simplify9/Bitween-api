using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SW.Bitween.Model.AutoRetry
{
    /// <summary>
    /// An adapter saying when to try again: an error whose message starts with
    /// <c>{{retryAfter:SECONDS}}</c>, as when a partner answered 429 with a Retry-After.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Text rather than a protocol field, because an adapter's error reaches Bitween as text from
    /// every runtime - built in, classic .NET, resident, Python, Node - so any adapter, on any SDK
    /// version, can say it just by how its message begins. The SDKs' RetryLater helpers only write
    /// it for you.
    /// </para>
    /// <para>
    /// Read only at the start of an exception's message - right after its type name - so a partner's
    /// response body quoted later in a message can't set it. And it only ever makes a retry the
    /// policy already allowed come later, up to a cap: it never makes one sooner, or makes one at all.
    /// </para>
    /// </remarks>
    public static class RetryAfterHint
    {
        static readonly Regex Marker = new(@"(?:Exception|Error): \{\{retryAfter:(\d{1,9})\}\}", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

        /// <summary>What an adapter starts its error message with to ask for <paramref name="after"/>.</summary>
        public static string Prefix(TimeSpan after) =>
            $"{{{{retryAfter:{Math.Max(0, (long)Math.Ceiling(after.TotalSeconds)).ToString(CultureInfo.InvariantCulture)}}}}}";

        /// <summary>The wait an error asks for, or null when it asks for none.</summary>
        /// <param name="error">The error as Bitween records it: the exception's type name, ": ", its message, and so on.</param>
        public static TimeSpan? Read(string error)
        {
            if (string.IsNullOrEmpty(error)) return null;
            try
            {
                var match = Marker.Match(error);
                return match.Success && long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                    ? TimeSpan.FromSeconds(seconds)
                    : null;
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }
        }
    }
}
