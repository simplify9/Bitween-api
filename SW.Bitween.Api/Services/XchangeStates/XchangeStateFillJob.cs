using System;
using System.Threading.Tasks;
using SW.Scheduler;

namespace SW.Bitween.Services.XchangeStates;

/// <summary>
/// Runs <see cref="XchangeStateFiller"/> every minute, on one node at a time. Once the fill is done
/// each run only reads one row.
/// </summary>
[ScheduleConfig(AllowConcurrentExecution = false, MisfireInstructions = MisfireInstructions.Skip)]
public class XchangeStateFillJob(XchangeStateFiller filler) : IScheduledJob
{
    /// <summary>How long one run works, so it ends before the next one is due.</summary>
    internal static readonly TimeSpan Budget = TimeSpan.FromSeconds(45);

    public Task Execute() => filler.RunAsync(Budget);
}
