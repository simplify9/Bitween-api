using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SW.Infolink.Web
{
    internal static class MappingExtensions
    {
        public static Dictionary<string, string> ToDictionary(this string Properties)
        {
            Dictionary<string, string> PromotedPro = new Dictionary<string, string>();
            if (Properties != null)
            {
                var lastIndexOfSemiColon = Properties.LastIndexOf(';');
                if (lastIndexOfSemiColon == Properties.Length - 1) { Properties = Properties.Remove(Properties.Length - 1); }
                var pairs = Properties.Split(';');

                foreach (var p in pairs)
                {
                    var proprety = p.Split(new[] { '=' }, 2, StringSplitOptions.None);
                    PromotedPro.Add(proprety[0].Trim(), proprety[1].Trim());
                }
            }
            return PromotedPro;
        }

        public static string FromDictionary(this IDictionary<string, string> props)
        {
            var properties = "";
            if (props != null)
            {
                foreach (var i in props)
                {
                    properties += $"{i.Key}={i.Value};";
                }
            }
            return properties;
        }

        public static ICollection<Schedule> ToSchedules(this string schedule, ICollection<Schedule> schedules = null)
        {
            if (schedules == null)
                schedules = new Collection<Schedule>();

            if (string.IsNullOrEmpty(schedule))
                return schedules;

            schedules.Clear();

            var keyFormat = schedule.Substring(0, schedule.IndexOf(';')).Split('=');
            var _schdule = new Schedule((Recurrence)Enum.Parse(typeof(Recurrence), keyFormat[0], true), TimeSpan.Parse(keyFormat[1]));
            schedules.Add(_schdule);
            schedule = schedule.Remove(0, schedule.IndexOf(';') + 1);
            ICollection<Schedule> sch = schedules;
            return sch.Concat(schedule.ToSchedules()).ToList();
        }

        public static string FromSchedules(this ICollection<Schedule> schedules)
        {
            var strSchedules = "";
            if (schedules != null)
            {
                foreach (var i in schedules)
                {
                    strSchedules += $"{i.Recurrence}={i.On};";
                }
            }
            return strSchedules;

        }
    }
}
