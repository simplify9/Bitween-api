using Humanizer;
using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink
{
    static class DateTimeExtensions
    {
        public static string Elapsed(this DateTime started, DateTime? finished)
        {
            if (finished == null)
                return DateTime.UtcNow.Subtract(started).Humanize();
            return finished.Value.Subtract(started).Humanize();
        }
    }
}
