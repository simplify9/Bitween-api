using SW.Bitween.Domain;
using SW.Bitween.Model;

namespace SW.Bitween;

// Quartz cron format: seconds minutes hours day-of-month month day-of-week
// day-of-week numbering: 1=SUN, 2=MON, 3=TUE, 4=WED, 5=THU, 6=FRI, 7=SAT
internal static class ScheduleToCronExtension
{
    public static string ToCronExpression(this Schedule schedule) => schedule.Recurrence switch
    {
        Recurrence.Hourly  => $"0 {schedule.On.Minutes} * * * ?",
        Recurrence.Daily   => $"0 {schedule.On.Minutes} {schedule.On.Hours} * * ?",
        Recurrence.Weekly  => $"0 {schedule.On.Minutes} {schedule.On.Hours} ? * {schedule.On.Days + 1}",
        Recurrence.Monthly => $"0 {schedule.On.Minutes} {schedule.On.Hours} {schedule.On.Days} * ?",
        _ => throw new BitweenException($"Unsupported recurrence: {schedule.Recurrence}")
    };

    // Stable, deterministic key for a (subscription, schedule) pair.
    // On.Ticks is used for exact precision; Backwards flag is included to avoid collisions.
    public static string ScheduleKeyFor(string prefix, int subscriptionId, Schedule schedule)
        => $"{prefix}-{subscriptionId}-{(int)schedule.Recurrence}-{schedule.On.Ticks}-{(schedule.Backwards ? 1 : 0)}";
}
