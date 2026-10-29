using System;

namespace SW.Bitween.Domain.Metrics;

/// <summary>
/// One hour of calls to one version of one adapter, in one role, on one runtime, as one node made
/// them. Counted where every adapter call goes through (AdapterInvoker), so receivers, validators,
/// mappers and handlers, native or not, are all here.
/// </summary>
public class AdapterStats : TimedStats
{
    public DateTime Hour { get; set; }
    public string Node { get; set; }
    public string AdapterId { get; set; }
    /// <summary>The version that ran; empty for a built-in adapter or one published before catalogs.</summary>
    public string Version { get; set; }
    /// <summary>receiver, validator, mapper, handler.</summary>
    public string Role { get; set; }
    /// <summary>native (in-process), resident (pooled process), process (one process per call).</summary>
    public string Runtime { get; set; }

    public int Calls { get; set; }
    /// <summary>Every call that failed, whatever the cause; the three below are some of them.</summary>
    public int Failures { get; set; }
    public int Timeouts { get; set; }
    /// <summary>Stopped by the runtime for going over a memory or CPU limit.</summary>
    public int LimitStops { get; set; }
    /// <summary>The adapter's process died or stopped answering.</summary>
    public int Crashes { get; set; }

    public void Add(AdapterStats other)
    {
        Calls += other.Calls;
        Failures += other.Failures;
        Timeouts += other.Timeouts;
        LimitStops += other.LimitStops;
        Crashes += other.Crashes;
        AddTimes(other);
    }
}
