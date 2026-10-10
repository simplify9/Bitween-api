import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Info, RefreshCw, TriangleAlert } from "lucide-react";
import { api, type RetentionNotice, type SettingRow } from "../../api";
import { keys } from "../../api/queryKeys";
import { Badge, Button } from "../../components/ui/basics";
import { useRetention } from "../../lib/retention";
import type { SettingsDraft } from "../../lib/settingsDraft";

const days = (n: number) => `${n} day${n === 1 ? "" : "s"}`;

const formatWhen = (iso: string | null) =>
  iso ? new Date(iso).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" }) : "—";

export function NoticeList({ notices }: { notices: RetentionNotice[] }) {
  return (
    <ul className="space-y-1.5">
      {notices.map((n) => (
        <li
          key={n.code}
          className={`flex items-start gap-2 rounded-lg px-3 py-2 text-[13px] ${
            n.level === "warning" ? "bg-warn-100/50 text-warn-800" : "bg-ink-50 text-ink-700"
          }`}
        >
          {n.level === "warning" ? (
            <TriangleAlert className="mt-0.5 size-3.5 shrink-0" aria-label="Warning" />
          ) : (
            <Info className="mt-0.5 size-3.5 shrink-0 text-ink-500" aria-label="Note" />
          )}
          <span>{n.message}</span>
        </li>
      ))}
    </ul>
  );
}

/** One figure of the summary strip: a label, its value, and what it means on hover. */
function Figure({ label, value, title }: { label: string; value: string; title: string }) {
  return (
    <div title={title} className="min-w-0">
      <dt className="text-[11px] font-medium tracking-wide text-ink-500 uppercase">{label}</dt>
      <dd className="mt-0.5 truncate text-sm font-medium text-ink-900">{value}</dd>
    </div>
  );
}

/**
 * Sits above the Documents & storage rows: what the bucket's own rules delete, and what the
 * retention settings do with them — for the saved values, or for staged ones before they're saved.
 */
export function RetentionPanel({ rows, draft }: { rows: SettingRow[]; draft: SettingsDraft }) {
  const queryClient = useQueryClient();
  const { status, previewing, loading, error } = useRetention(rows, draft);
  const [refreshing, setRefreshing] = useState(false);

  const refresh = async () => {
    setRefreshing(true);
    try {
      queryClient.setQueryData(keys.settings.retention, await api.getRetention(true));
      void queryClient.invalidateQueries({ queryKey: keys.settings.retention, exact: false });
    } finally {
      setRefreshing(false);
    }
  };

  if (!status) {
    return (
      <p className="mb-4 text-sm text-ink-500">
        {error ? `Couldn't load how long exchanges are kept: ${error.message}` : "Loading retention…"}
      </p>
    );
  }

  const filesRulePrefix = status.files.rulePrefix;
  const ex = status.exchanges;
  const count = (n: number | null) => (n === null ? "—" : n >= ex.countCap ? `${ex.countCap.toLocaleString()}+` : n.toLocaleString());

  return (
    <div className="mb-5 space-y-4 border-b border-ink-100 pb-5">
      <dl className="grid grid-cols-2 gap-x-5 gap-y-3 sm:grid-cols-4">
        <Figure
          label="Files kept"
          value={status.files.days === null ? (status.storage.problem ? "Unknown" : "For ever") : days(status.files.days)}
          title={`How long the bucket keeps files written under ${status.files.prefix}/ — decided by the bucket's own rule, not by Bitween.`}
        />
        <Figure
          label="Exchanges kept"
          value={ex.retentionDays === 0 ? "For ever" : days(ex.retentionDays)}
          title="How long exchanges stay on the Exchanges page before the retention job removes them."
        />
        <Figure
          label="Next run"
          value={ex.retentionDays === 0 ? "Off" : formatWhen(ex.nextRun)}
          title={`When the retention job next runs (${ex.cron}), in your time zone. It does nothing while exchanges are kept for ever.`}
        />
        <Figure
          label="Without files now"
          value={count(ex.withoutFiles)}
          title="Exchanges still listed whose files the bucket has already deleted: they can't be opened or retried."
        />
      </dl>

      <div>
        <div className="mb-2 flex items-center justify-between gap-2">
          <h3 className="text-[13px] font-semibold text-ink-800">
            {previewing ? "What your unsaved changes would do" : "What these settings do"}
          </h3>
          {loading && <span className="text-xs text-ink-500">Updating…</span>}
        </div>
        <NoticeList notices={status.notices} />
      </div>

      <div>
        <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
          <h3 className="text-[13px] font-semibold text-ink-800">
            Bucket deletion rules
            {status.storage.provider && (
              <span className="ml-2 font-normal text-ink-500">
                {status.storage.provider} · {status.storage.bucket}
              </span>
            )}
          </h3>
          <Button
            size="sm"
            variant="ghost"
            busy={refreshing}
            onClick={() => void refresh()}
            title="Ask the storage service for its rules again — they're otherwise read at most every few minutes"
          >
            <RefreshCw className="size-3.5" aria-hidden />
            Refresh
          </Button>
        </div>
        {status.storage.problem ? (
          <p className="rounded-lg bg-warn-100/50 px-3 py-2 text-[13px] text-warn-800">{status.storage.problem}</p>
        ) : status.storage.rules.length === 0 ? (
          <p className="text-[13px] text-ink-500">The bucket has no deletion rules, so nothing in it is ever deleted.</p>
        ) : (
          <table className="w-full max-w-lg text-sm">
            <thead>
              <tr className="text-left text-xs text-ink-500">
                <th className="pb-1 pr-4 font-medium" title="Files whose storage key starts with this are covered">
                  Prefix
                </th>
                <th className="pb-1 pr-4 font-medium" title="Days after a file is written that the bucket deletes it">
                  Deletes after
                </th>
                <th className="pb-1 font-medium" title="A disabled rule is listed but deletes nothing" />
              </tr>
            </thead>
            <tbody>
              {status.storage.rules.map((rule) => (
                <tr key={`${rule.id}-${rule.prefix}`} className="border-t border-ink-100">
                  <td className="py-1.5 pr-4 font-mono text-[13px] text-ink-800">{rule.prefix || "(everything)"}</td>
                  <td className="py-1.5 pr-4 tabular-nums text-ink-700">{days(rule.days)}</td>
                  <td className="py-1.5">
                    <div className="flex flex-wrap gap-1">
                      {!rule.enabled && (
                        <Badge title="The storage service has this rule switched off">Disabled</Badge>
                      )}
                      {rule.enabled && rule.prefix === filesRulePrefix && (
                        <Badge tone="crimson" title={`New exchange files are written under ${status.files.prefix}/, so this rule deletes them`}>
                          Exchange files
                        </Badge>
                      )}
                      {rule.enabled && rule.prefix === status.archive.rulePrefix && (
                        <Badge tone="warn" title={`Archives are written under ${status.archive.prefix}/, so this rule deletes them too`}>
                          Archive
                        </Badge>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        <p className="mt-2 text-xs text-ink-500">
          Archived exchanges go to <code className="font-mono">{status.archive.prefix}/</code>, filed by subscription
          and day and named by the first promoted property.
        </p>
      </div>
    </div>
  );
}
