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
                    throw new ArgumentException();
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

        public DateTime Next(DateTime currentDate = default)
        {
            //if (Recurrence == Recurrence.None) throw new ArgumentException();

            var now = currentDate == default ? DateTime.UtcNow : currentDate;
            DateTime nextSelectedDate = default; // = null;

            switch (Recurrence)
            {
                case Recurrence.Hourly:
                    //dates = ;
                    nextSelectedDate = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).Add(On);//  (onminutes).AddHours(1);
                    if (nextSelectedDate < now) nextSelectedDate.AddHours(1);
                    break;

                case Recurrence.Daily:
                    //dates = onminutes.Select(e => new DateTime(now.Year, now.Month, now.Day, 0, 0, 0).AddMinutes(e));
                    nextSelectedDate = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0).Add(On);
                    if (nextSelectedDate < now) nextSelectedDate.AddDays(1);
                    break;

                case Recurrence.Weekly:
                    //DayOfWeek.Sunday =0
                    nextSelectedDate = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0).AddDays(-(byte)now.DayOfWeek).Add(On);
                    if (nextSelectedDate < now) nextSelectedDate.AddDays(7);
                    //dates = onminutes.Select(e => new DateTime(now.Year, now.Month, 18, 0, 0, 0).AddDays(-(byte)(DayOfWeek.Wednesday)).AddMinutes(e));
                    //var datesSubFromNowWeekly = dates.Select(e => e).Where(e => DateTime.Compare(e, DateTime.UtcNow) > 0);
                    //if (datesSubFromNowWeekly.Count() > 0) nextSelectedDate = datesSubFromNowWeekly.First();
                    //if (nextSelectedDate == null) nextSelectedDate = dates.First().AddDays(7);

                    break;

                case Recurrence.Monthly:
                    nextSelectedDate = new DateTime(now.Year, now.Month, 1, 0, 0, 0).Add(On);
                    if (nextSelectedDate < now) nextSelectedDate.AddMonths(1);
                    //var datesSubFromNowMonthly = dates.Select(e => e).Where(e => DateTime.Compare(e, DateTime.UtcNow) > 0);//.First().AddMonths(1 );
                    //if (datesSubFromNowMonthly.Count() > 0) nextSelectedDate = datesSubFromNowMonthly.First();
                    //if (nextSelectedDate == null) nextSelectedDate = dates.First().AddMonths(1);
                    break;
            }

            //DateTime selectedDate = dates.Where(d => d > now).FirstOrDefault();

            //if (selectedDate == default) selectedDate = nextSelectedDate.Value;

            return nextSelectedDate;
        }
    }



}
