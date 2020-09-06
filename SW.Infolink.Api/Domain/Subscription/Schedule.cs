using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;

namespace SW.Infolink.Domain
{
    public class Schedule
    {
        public Schedule(Recurrence recurrence, TimeSpan on, bool backwards = false)
        {
            Recurrence = recurrence;
            On = on;
            Backwards = backwards;

            switch (Recurrence)
            {
                case Recurrence.Hourly:
                    if (on == null) throw new ArgumentException();
                    if (on.TotalHours >= 1) throw new ArgumentException();
                    break;

                case Recurrence.Daily:
                    if (on == null) throw new ArgumentException();
                    if (on.TotalDays >= 1) throw new ArgumentException();
                    break;

                case Recurrence.Weekly:
                    if (on == null) throw new ArgumentException();
                    if (on.TotalDays >= 7) throw new ArgumentException();
                    break;

                case Recurrence.Monthly:
                    if (on == null) throw new ArgumentException();
                    if (on.TotalDays >= 28) throw new ArgumentException();
                    break;

                default:
                    throw new InfolinkException();
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
                    nextSelectedDate = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, 0, 0, DateTimeKind.Utc).Add(On);
                    if (nextSelectedDate < utcNow) 
                        nextSelectedDate = nextSelectedDate.AddHours(1);
                    return nextSelectedDate;

                case Recurrence.Daily:
                    nextSelectedDate = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, 0, 0, 0, DateTimeKind.Utc).Add(On);
                    if (nextSelectedDate < utcNow) 
                        nextSelectedDate = nextSelectedDate.AddDays(1);
                    return nextSelectedDate;

                case Recurrence.Weekly:
                    nextSelectedDate = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(-(byte)utcNow.DayOfWeek).Add(On);
                    if (nextSelectedDate < utcNow) 
                        nextSelectedDate = nextSelectedDate.AddDays(7);
                    return nextSelectedDate;

                case Recurrence.Monthly:
                    TimeSpan _on = On;
                    if (_on.TotalDays >= 1)
                        _on = On.Subtract(TimeSpan.FromDays(1));
                    nextSelectedDate = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).Add(_on);
                    if (nextSelectedDate < utcNow) 
                        nextSelectedDate = nextSelectedDate.AddMonths(1);
                    return nextSelectedDate;

                default:
                    throw new InfolinkException();
            }
        }
    }



}
