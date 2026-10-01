using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Quartz;
using SW.Bitween.Domain;
using SW.Bitween.Model;

namespace SW.Bitween;

/// <summary>
/// Works out what the retention settings do — as they stand, or as someone is about to change them — so
/// the settings page can say it before anything is saved: how long files and exchanges are kept, what
/// the next run removes, and which exchanges are left without their files.
/// </summary>
public class RetentionPlanner(BitweenDbContext dbContext, BitweenOptions options, StorageRetention storageRetention,
    FileLinks fileLinks, StorageAccess storageAccess)
{
    /// <summary>Counting past this on a large table costs more than the number is worth.</summary>
    public const int CountCap = 100_000;

    public async Task<RetentionStatus> Plan(RetentionProposal proposal = null, bool refresh = false)
    {
        var prefix = CleanPrefix(proposal?.DocumentPrefix) ?? options.DocumentPrefix;
        var days = Math.Max(0, proposal?.ExchangeRetentionDays ?? options.ExchangeRetentionDays);
        var archive = proposal?.ArchiveExchanges ?? options.ArchiveExchanges;
        var cron = string.IsNullOrWhiteSpace(proposal?.ExchangeRetentionCron)
            ? options.ExchangeRetentionCron
            : proposal.ExchangeRetentionCron.Trim();
        var publicUrl = proposal?.PublicUrl != null ? proposal.PublicUrl.Trim().TrimEnd('/') : options.PublicUrl;

        var rules = await storageRetention.GetAsync(refresh);
        var openToAnyone = await storageAccess.IsOpenToAnyoneAsync(refresh);
        var now = DateTime.UtcNow;

        var files = Retention(prefix, rules);
        // Exchanges already written keep the prefix they were written under, so how many are left
        // without files depends on the prefix in force now, not on a proposed one.
        var currentFiles = prefix == options.DocumentPrefix ? files : Retention(options.DocumentPrefix, rules);
        var legacyPrefix = options.LegacyDocumentPrefix;
        var legacyFiles = !string.IsNullOrEmpty(legacyPrefix) && legacyPrefix != prefix ? Retention(legacyPrefix, rules) : null;
        var archivePrefix = ExchangeArchive.PrefixFor(prefix);
        var archiveRetention = Retention(archivePrefix, rules);

        var nextRun = CronExpression.IsValidExpression(cron)
            ? new CronExpression(cron).GetNextValidTimeAfter(DateTimeOffset.UtcNow)
            : null;

        int? dueNow = days > 0
            ? await ExchangeRetention.DueBefore(dbContext, now.AddDays(-days)).Take(CountCap).CountAsync()
            : null;

        // As things stand now, whatever the proposal: those the next run removes are still listed until then.
        int? withoutFiles = null;
        if (currentFiles.Days is { } filesDays)
        {
            var filesGone = now.AddDays(-filesDays);
            withoutFiles = await dbContext.Set<Xchange>()
                .Where(x => x.StartedOn < filesGone)
                .Take(CountCap).CountAsync();
        }

        var oldest = await dbContext.Set<Xchange>().OrderBy(x => x.StartedOn)
            .Select(x => (DateTime?)x.StartedOn).FirstOrDefaultAsync();

        var hasAggregations = await dbContext.Set<Subscription>()
            .AnyAsync(s => s.Type == SubscriptionType.Aggregation && !s.Inactive);

        var status = new RetentionStatus
        {
            Storage = new StorageRulesInfo
            {
                Provider = rules.Lifecycle?.Provider,
                Bucket = rules.Lifecycle?.Bucket,
                Problem = rules.Problem,
                Rules = (rules.Lifecycle?.Rules ?? [])
                    .Select(r => new StorageRuleInfo { Id = r.Id, Prefix = r.Prefix, Days = r.Days, Enabled = r.Enabled })
                    .ToList()
            },
            Files = files,
            LegacyFiles = legacyFiles,
            Archive = archiveRetention,
            Exchanges = new ExchangeRetentionInfo
            {
                RetentionDays = days,
                Archive = archive,
                Cron = cron,
                NextRun = nextRun,
                DueNow = dueNow,
                WithoutFiles = withoutFiles,
                CountCap = CountCap,
                Oldest = oldest
            },
            PublicUrl = string.IsNullOrEmpty(publicUrl) ? null : publicUrl
        };

        status.Notices = Notices(status, currentFiles, prefix != options.DocumentPrefix, hasAggregations, openToAnyone == true);
        return status;
    }

    private List<RetentionNotice> Notices(RetentionStatus status, PrefixRetention currentFiles,
        bool prefixChanges, bool hasAggregations, bool openToAnyone)
    {
        var warnings = new List<RetentionNotice>();
        var info = new List<RetentionNotice>();
        var exchanges = status.Exchanges;
        var days = exchanges.RetentionDays;

        if (openToAnyone)
            warnings.Add(Warning("FILES_PUBLIC",
                "Bitween's files open without credentials: anyone with a file's address can read exchange files and archives. " +
                (storageAccess.CouldNotMakePrivate is { } reason
                    ? $"Bitween tried to make its container private and couldn't ({reason}); set the container's access level to Private in Azure."
                    : "The bucket is open to everyone; make it private in the storage provider's console.")));

        if (status.Storage.Problem != null)
            warnings.Add(Warning("RULES_UNKNOWN",
                $"The bucket's deletion rules couldn't be read, so how long files are kept can't be shown. {status.Storage.Problem}"));

        if (exchanges.NextRun == null)
            warnings.Add(Warning("SCHEDULE_INVALID",
                $"'{exchanges.Cron}' isn't a valid cron expression, so the retention job wouldn't run."));

        var newFilesDays = status.Files.Days;
        var newOutliveFiles = prefixChanges && newFilesDays is { } nd && (days == 0 || days > nd) &&
                              newFilesDays != currentFiles.Days;
        if (currentFiles.Days is { } filesDays && (days == 0 || days > filesDays))
            warnings.Add(Warning("OUTLIVE_FILES",
                $"Exchanges older than {filesDays} days are listed without their files: they can't be opened " +
                $"or retried. {(exchanges.WithoutFiles == 1 ? "There is 1" : $"There are {Count(exchanges.WithoutFiles, exchanges.CountCap)}")} right now." +
                (newOutliveFiles ? $" New exchanges lose theirs after {newFilesDays} days." : "")));
        else if (newOutliveFiles)
            warnings.Add(Warning("OUTLIVE_FILES",
                $"New exchanges will be listed without their files after {newFilesDays} days: they can't be " +
                "opened or retried from then on."));

        if (exchanges.Archive && days > 0 && newFilesDays is { } archivedFilesDays && days > archivedFilesDays)
            warnings.Add(Warning("ARCHIVE_WITHOUT_FILES",
                $"Archives won't hold the files: the bucket deletes them after {archivedFilesDays} days, before " +
                $"exchanges are archived at {days}."));

        if (exchanges.Archive && days > 0 && status.Archive.Days is { } archiveDays)
            warnings.Add(Warning("ARCHIVE_EXPIRES",
                $"The bucket deletes archives too: files under {status.Archive.RulePrefix} are kept {archiveDays} days."));

        if (hasAggregations && status.PublicUrl == null)
            info.Add(Info("NO_PUBLIC_ADDRESS",
                $"Aggregation roll-ups link to each file through {fileLinks.InstanceUrl ?? "this instance's own address"}: " +
                "adapters running in Bitween can open those links, nothing outside it can. Set the Public address " +
                "if a partner opens them."));

        if (prefixChanges)
            info.Add(Info("PREFIX_CHANGE",
                $"New exchanges' files go under {status.Files.Prefix}/ and " +
                (status.Files.Days is { } d ? $"are deleted after {d} days." : "no bucket rule deletes them by age.") +
                " Files already written stay where they are, under their own rule."));
        else if (status.Storage.Problem == null && status.Files.Days == null)
            info.Add(Info("FILES_KEPT",
                $"No bucket rule deletes files under {status.Files.Prefix}/ by age, so they stay until something else removes them."));

        if (status.LegacyFiles != null)
            info.Add(Info("LEGACY_PREFIX",
                $"Exchanges created before this version keep their files under {status.LegacyFiles.Prefix}/" +
                (status.LegacyFiles.Days is { } legacyDays ? $", deleted after {legacyDays} days." : ".")));

        info.Add(days == 0
            ? Info("KEEP_FOREVER", "Exchanges are kept for ever: nothing removes them from the database.")
            : Info("REMOVES",
                $"Each run removes exchanges that started more than {days} days ago" +
                (exchanges.Archive ? $", after copying them to {status.Archive.Prefix}/" : ", without archiving them") +
                (exchanges.NextRun is { } next
                    ? $". The next run, {next.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC, removes {Count(exchanges.DueNow, exchanges.CountCap)}."
                    : ".")));

        return warnings.Concat(info).ToList();
    }

    private static PrefixRetention Retention(string prefix, StorageRules rules)
    {
        var rule = rules.RuleForPrefix(prefix);
        return new PrefixRetention { Prefix = prefix, Days = rule?.Days, RulePrefix = rule?.Prefix };
    }

    private static string Count(int? count, int cap) =>
        count is null or 0 ? "none"
        : count >= cap ? cap.ToString("N0", CultureInfo.InvariantCulture) + "+"
        : count.Value.ToString("N0", CultureInfo.InvariantCulture);

    private static string CleanPrefix(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('/');

    private static RetentionNotice Warning(string code, string message) => new() { Level = "warning", Code = code, Message = message };
    private static RetentionNotice Info(string code, string message) => new() { Level = "info", Code = code, Message = message };
}
