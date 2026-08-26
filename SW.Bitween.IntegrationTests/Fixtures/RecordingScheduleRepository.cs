using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using SW.Scheduler;

namespace SW.Bitween.IntegrationTests.Fixtures;

/// <summary>
/// Stands in for Quartz, recording what would have been scheduled instead of scheduling it.
/// </summary>
/// <remarks>
/// The handlers that create and update integrations register their schedules through this, so
/// without an implementation they can't be constructed at all. A real Quartz job store would work
/// — the migrations create its tables — but it would put a background scheduler behind every test
/// in the collection, firing real jobs against shared state at unpredictable moments. Whether
/// Quartz itself schedules correctly is a separate question, and the receiving and retry tests
/// already answer it by driving the jobs directly.
/// </remarks>
internal sealed class RecordingScheduleRepository : IScheduleRepository
{
    /// <summary>Schedule keys currently registered, so a test can assert on them if it needs to.</summary>
    public ConcurrentDictionary<string, string> Scheduled { get; } = new();

    public Task Schedule<TScheduler, TParam>(TParam param, string cronExpression, string scheduleKey,
        ScheduleConfig? config = null) where TScheduler : IScheduledJob<TParam>
    {
        Scheduled[scheduleKey] = cronExpression;
        return Task.CompletedTask;
    }

    public Task Schedule<TScheduler>(string cronExpression, ScheduleConfig? config = null)
        where TScheduler : IScheduledJob
    {
        Scheduled[typeof(TScheduler).FullName!] = cronExpression;
        return Task.CompletedTask;
    }

    public Task<string> ScheduleOnce<TScheduler, TParam>(TParam param, DateTime? runAt = null,
        ScheduleConfig? config = null) where TScheduler : IScheduledJob<TParam>
        => Task.FromResult(Guid.NewGuid().ToString("N"));

    public Task RescheduleJob<TScheduler, TParam>(string scheduleKey, string newCronExpression)
        where TScheduler : IScheduledJob<TParam>
    {
        Scheduled[scheduleKey] = newCronExpression;
        return Task.CompletedTask;
    }

    public Task RescheduleJob<TScheduler>(string newCronExpression) where TScheduler : IScheduledJob
    {
        Scheduled[typeof(TScheduler).FullName!] = newCronExpression;
        return Task.CompletedTask;
    }

    public Task UnscheduleJob<TScheduler, TParam>(string scheduleKey) where TScheduler : IScheduledJob<TParam>
    {
        Scheduled.TryRemove(scheduleKey, out _);
        return Task.CompletedTask;
    }

    public Task UnscheduleJob<TScheduler>() where TScheduler : IScheduledJob
    {
        Scheduled.TryRemove(typeof(TScheduler).FullName!, out _);
        return Task.CompletedTask;
    }

    public Task PauseJob<TScheduler, TParam>(string scheduleKey) where TScheduler : IScheduledJob<TParam>
        => Task.CompletedTask;

    public Task PauseJob<TScheduler>() where TScheduler : IScheduledJob => Task.CompletedTask;

    public Task ResumeJob<TScheduler, TParam>(string scheduleKey) where TScheduler : IScheduledJob<TParam>
        => Task.CompletedTask;

    public Task ResumeJob<TScheduler>() where TScheduler : IScheduledJob => Task.CompletedTask;

    public Task<bool> ScheduleIfNotExists<TScheduler, TParam>(TParam param, string cronExpression,
        string scheduleKey, ScheduleConfig? config = null) where TScheduler : IScheduledJob<TParam>
        => Task.FromResult(Scheduled.TryAdd(scheduleKey, cronExpression));

    /// <summary>Job discovery happens at startup against the real scheduler; nothing here needs it.</summary>
    public IEnumerable<IScheduledJobDefinition> GetJobDefinitions() => [];
}
