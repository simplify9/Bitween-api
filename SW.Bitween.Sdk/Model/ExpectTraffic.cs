using System.Collections.Generic;

namespace SW.Bitween.Model
{
    /// <summary>
    /// How often a subscription should see an exchange. When none arrives for longer than
    /// <see cref="EveryMinutes"/> inside the window, the subscription is flagged as gone quiet and the
    /// notifiers watching it for failures are told: a partner that stops sending fails nothing.
    /// </summary>
    public class ExpectTraffic
    {
        /// <summary>At least one exchange this often, in minutes. Five or more.</summary>
        public int EveryMinutes { get; set; }

        /// <summary>Sun, Mon, Tue, Wed, Thu, Fri, Sat. Null or empty means every day.</summary>
        public ICollection<string>? Days { get; set; }

        /// <summary>
        /// Local time of day the window opens, HH:mm. Set with <see cref="To"/>, or neither for the
        /// whole day. A window can't run past midnight.
        /// </summary>
        public string? From { get; set; }

        /// <summary>Local time of day the window closes, HH:mm, later than <see cref="From"/>.</summary>
        public string? To { get; set; }

        /// <summary>IANA time zone the days and times are in, e.g. Asia/Riyadh. Null means UTC.</summary>
        public string? TimeZone { get; set; }
    }
}
