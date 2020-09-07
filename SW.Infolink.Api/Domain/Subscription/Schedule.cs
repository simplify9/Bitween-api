using SW.Infolink.Model;
using System;

namespace SW.Infolink.Domain
{
    public class Schedule
    {
        private Schedule()
        {
        }

        public Schedule(Recurrence recurrence, TimeSpan on, bool backwards = false)
        {
            Recurrence = recurrence;
            On = on;
            Backwards = backwards;

            switch (Recurrence)
            {
                case Recurrence.Hourly:
                    if (on.TotalHours >= 1) throw new InfolinkException();
                    break;

                case Recurrence.Daily:
                    if (on.TotalDays >= 1) throw new InfolinkException();
                    break;

                case Recurrence.Weekly:
                    if (on.TotalDays >= 7) throw new InfolinkException();
                    break;

                case Recurrence.Monthly:
                    if (on.TotalDays >= 28) throw new InfolinkException();
                    if (on.Days < 1) throw new InfolinkException();
                    break;

                //default:
                    //throw new InfolinkException();
            }
        }
        public Recurrence Recurrence { get; private set; }
        public TimeSpan On { get; private set; }
        public bool Backwards { get; private set; }

        public override bool Equals(object obj)
        {
            return obj is Schedule schedule &&
                   Recurrence == schedule.Recurrence &&
                   On.Equals(schedule.On) &&
                   Backwards == schedule.Backwards;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Recurrence, On, Backwards);
        }

        public DateTime Next(DateTime? currentDate = null)
        {
            var utcNow = currentDate == null ? DateTime.UtcNow : currentDate.Value;
            DateTime nextSelectedDate;

            switch (Recurrence)
            {
                case Recurrence.Hourly:
                    nextSelectedDate = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, On.Minutes, 0, DateTimeKind.Utc);
                    if (nextSelectedDate < utcNow)
                        nextSelectedDate = nextSelectedDate.AddHours(1);
                    return nextSelectedDate;

                case Recurrence.Daily:
                    nextSelectedDate = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, On.Hours, On.Minutes, 0, DateTimeKind.Utc);
                    if (nextSelectedDate < utcNow)
                        nextSelectedDate = nextSelectedDate.AddDays(1);
                    return nextSelectedDate;

                case Recurrence.Weekly:
                    nextSelectedDate = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(-(byte)utcNow.DayOfWeek).Add(On);
                    if (nextSelectedDate < utcNow)
                        nextSelectedDate = nextSelectedDate.AddDays(7);
                    return nextSelectedDate;

                case Recurrence.Monthly:
                    nextSelectedDate = new DateTime(utcNow.Year, utcNow.Month, On.Days, On.Hours, On.Minutes, 0, DateTimeKind.Utc);
                    if (nextSelectedDate < utcNow)
                        nextSelectedDate = nextSelectedDate.AddMonths(1);
                    return nextSelectedDate;

                default:
                    throw new InfolinkException();
            }
        }
    }



}
