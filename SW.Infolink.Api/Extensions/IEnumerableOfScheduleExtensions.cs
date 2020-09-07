using SW.Infolink.Domain;
using SW.Infolink.Model;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace SW.Infolink
{
    internal static class IEnumerableOfScheduleExtensions
    {
        public static DateTime? Next(this IEnumerable<Schedule> schedules)
        {
            DateTime? nextSchedule = null;
            foreach (var sched in schedules)
            {
                var tmpNext = sched.Next();
                if (nextSchedule == null || tmpNext < nextSchedule)
                    nextSchedule = tmpNext;
            }
            return nextSchedule;
        }
    }
}
