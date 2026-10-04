using System;
using System.Collections.Generic;

namespace SW.Bitween.Model;

/// <summary>Retention settings to preview before saving them. Anything left out keeps its current value.</summary>
public class RetentionProposal
{
    public string? DocumentPrefix { get; set; }
    public int? ExchangeRetentionDays { get; set; }
    public bool? ArchiveExchanges { get; set; }
    public string? ExchangeRetentionCron { get; set; }
}

public class RetentionStatusRequest
{
    /// <summary>Ask the storage service for its rules again rather than use the few-minutes-old copy.</summary>
    public bool Refresh { get; set; }
}

/// <summary>How long exchanges and their files are kept, and what the settings will do — now or as proposed.</summary>
public class RetentionStatus
{
    public StorageRulesInfo Storage { get; set; } = null!;

    /// <summary>Where new exchange files are written, and how long the bucket keeps them.</summary>
    public PrefixRetention Files { get; set; } = null!;

    /// <summary>
    /// Where exchanges from before exchanges recorded their own prefix keep their files, when that isn't
    /// <see cref="Files"/>; otherwise null.
    /// </summary>
    public PrefixRetention? LegacyFiles { get; set; }

    /// <summary>Where the retention job archives exchanges, and whether the bucket deletes archives too.</summary>
    public PrefixRetention Archive { get; set; } = null!;

    public ExchangeRetentionInfo Exchanges { get; set; } = null!;

    /// <summary>What the settings mean in practice, worst first: warnings, then plain consequences.</summary>
    public List<RetentionNotice> Notices { get; set; } = new();
}

public class StorageRulesInfo
{
    public string? Provider { get; set; }
    public string? Bucket { get; set; }

    /// <summary>Why the bucket's rules couldn't be read; null when <see cref="Rules"/> is the real rule set.</summary>
    public string? Problem { get; set; }

    public List<StorageRuleInfo> Rules { get; set; } = new();
}

public class StorageRuleInfo
{
    public string? Id { get; set; }
    public string Prefix { get; set; } = null!;
    public int Days { get; set; }
    public bool Enabled { get; set; }
}

public class PrefixRetention
{
    public string Prefix { get; set; } = null!;

    /// <summary>Days before the bucket deletes files under the prefix; null when no rule deletes them by age (or the rules are unknown).</summary>
    public int? Days { get; set; }

    /// <summary>The prefix of the bucket rule that applies, when one does.</summary>
    public string? RulePrefix { get; set; }
}

public class ExchangeRetentionInfo
{
    /// <summary>0 keeps exchanges for ever.</summary>
    public int RetentionDays { get; set; }

    public bool Archive { get; set; }
    public string Cron { get; set; } = null!;

    /// <summary>When the retention job next runs; null for an expression that isn't valid.</summary>
    public DateTimeOffset? NextRun { get; set; }

    /// <summary>Exchanges the next run would remove; null while retention is off.</summary>
    public int? DueNow { get; set; }

    /// <summary>
    /// Exchanges still kept but older than their files: listed without them. Null when the bucket keeps
    /// files for ever or its rules are unknown.
    /// </summary>
    public int? WithoutFiles { get; set; }

    /// <summary>Counts stop at this many; a count equal to it means "at least".</summary>
    public int CountCap { get; set; }

    public DateTime? Oldest { get; set; }
}

/// <summary>Aggregation schedules to check against how long exchanges are kept, before they're saved.</summary>
public class AggregationRetentionRequest
{
    public List<ScheduleView> Schedules { get; set; } = new();
}

public class AggregationRetentionCheck
{
    /// <summary>Why exchanges would be removed before this aggregation collects them; null when they won't be.</summary>
    public string? Warning { get; set; }
}

public class RetentionNotice
{
    /// <summary>"warning" or "info".</summary>
    public string Level { get; set; } = null!;

    public string Code { get; set; } = null!;
    public string Message { get; set; } = null!;
}
