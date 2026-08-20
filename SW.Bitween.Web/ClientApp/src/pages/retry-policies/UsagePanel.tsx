import { useState } from "react";
import { Link } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, BellOff, ChevronDown, ChevronRight, RotateCcw } from "lucide-react";
import { api, type IntegrationSetupRef, type RetryAlertLevel, type RetryUsageRow } from "../../api";
import { Badge, Button, FormError, LoadingBlock } from "../../components/ui/basics";
import { ConfirmDialog, Dialog } from "../../components/ui/overlays";
import { Panel } from "../../components/ui/Panel";
import { formatDateTime, timeAgo } from "../../lib/dates";
import { AlertRouting } from "./AlertRouting";

/**
 * What each retry budget of this policy has actually spent, and whether anyone was told when
 * one ran out.
 *
 * One row per integration-and-group pair, because that is how a budget is counted: a shared
 * policy gives every integration its own separate total. There is no such thing as "this
 * policy's usage" — any single figure here would be an aggregate matching nothing anyone can
 * act on — and it is also why resetting and overriding both address one pair.
 *
 * Everything in this panel happens immediately. The groups above it stage into the save bar;
 * a reset here does not, and the panel says so rather than leaving the reader to find out.
 */

const LEVEL_WORD: Record<RetryAlertLevel, string> = {
  SubscriptionGroup: "this integration",
  Group: "the group",
  Policy: "the policy",
};

type FilterKey = "attention" | "exhausted" | "silent" | "overridden" | "all";

/**
 * A pair worth looking at: stopped retrying, alerted nobody when it did, or would alert
 * nobody without anyone having chosen that.
 *
 * A silence someone configured is deliberately *not* here. Both a silenced pair and an
 * unrouted one send nothing, but only one of them is a mistake, and a filter that cannot
 * tell them apart flags every deliberate silence until nobody reads it any more.
 */
const needsAttention = (r: RetryUsageRow) =>
  r.exhausted || r.alert?.delivered === false || (r.resolvedHandlerId === null && r.silencedAt === null);

const FILTERS: { key: FilterKey; label: string; match: (r: RetryUsageRow) => boolean; blurb: string }[] = [
  {
    key: "attention",
    label: "Needs attention",
    match: needsAttention,
    blurb:
      "Budgets that have run out, alerts that did not arrive, and pairs that would alert nobody without anyone having chosen that.",
  },
  {
    key: "exhausted",
    label: "Exhausted",
    match: (r) => r.exhausted,
    blurb: "No longer retried at all until the budget is reset, or the integration succeeds.",
  },
  {
    key: "silent",
    label: "No alert",
    match: (r) => r.resolvedHandlerId === null,
    blurb: "Nothing is sent when these run out — whether that was chosen or simply never set.",
  },
  {
    key: "overridden",
    label: "Overridden",
    match: (r) => r.override.alertMode !== "Inherit",
    blurb: "Pairs whose alert routing is set on the integration itself, not inherited.",
  },
  { key: "all", label: "All", match: () => true, blurb: "Every integration and group using this policy." },
];

/** Where this pair's alert ends up, said as a destination rather than a mode. */
function AlertCell({ row }: { row: RetryUsageRow }) {
  if (row.resolvedHandlerId)
    return (
      <span className="text-[13px] text-ink-700">
        <span className="font-mono text-xs">{row.resolvedHandlerId}</span>
        {row.resolvedFrom && <span className="text-ink-400"> · set by {LEVEL_WORD[row.resolvedFrom]}</span>}
      </span>
    );

  return (
    <span className="inline-flex items-center gap-1.5 text-[13px]">
      <BellOff className="size-3.5 text-ink-400" />
      {row.silencedAt ? (
        <span className="text-ink-500">Silenced by {LEVEL_WORD[row.silencedAt]}</span>
      ) : (
        <span className="font-medium text-warn-700">Nobody</span>
      )}
    </span>
  );
}

/**
 * Whether the alert reached anyone.
 *
 * Kept apart from the fact that one was raised, because those are two different things and
 * the page has to be able to say when they disagree: the alert is claimed *before* the send is
 * attempted, so a refused login or a failed TLS handshake leaves a budget that stopped
 * retrying and a team that was never told.
 */
function AlertedCell({ row }: { row: RetryUsageRow }) {
  const { alert } = row;
  if (!alert) return <span className="text-ink-400">—</span>;
  if (alert.delivered === false) return <Badge tone="danger">Not delivered</Badge>;
  if (alert.delivered === null)
    return (
      <span title="The alert was raised, but no delivery was recorded against it.">
        <Badge tone="warn">Unconfirmed</Badge>
      </span>
    );
  return (
    <span className="text-[13px] text-ink-600" title={formatDateTime(alert.claimedOn)}>
      Sent {timeAgo(alert.claimedOn)}
    </span>
  );
}

/** The failures a pair spent its budget on, fetched only once the row is opened. */
function Attempts({ policyId, row }: { policyId: number; row: RetryUsageRow }) {
  const q = useQuery({
    queryKey: ["retry-attempts", policyId, row.integrationId, row.groupId],
    queryFn: () => api.getRetryAttempts(policyId, { integrationId: row.integrationId, groupId: row.groupId }),
  });

  if (q.isPending) return <LoadingBlock label="Loading failures…" />;
  if (q.isError) return <FormError>{q.error.message}</FormError>;

  const { total, attempts } = q.data;
  if (attempts.length === 0)
    return (
      <p className="text-[13px] text-ink-500">
        No failures recorded against this group for this integration.
      </p>
    );

  return (
    <div className="space-y-2">
      {row.alert?.error && (
        <div className="rounded-lg border border-danger-200 bg-danger-50 px-3 py-2">
          <p className="text-[13px] font-medium text-danger-800">
            The budget-exhausted alert could not be delivered.
          </p>
          <p className="mt-1 font-mono text-xs break-words text-danger-700">{row.alert.error}</p>
        </div>
      )}
      <ol className="space-y-1">
        {attempts.map((a) => (
          <li key={a.exchangeId} className="flex items-start gap-2.5 text-[13px]">
            <Link
              to={`/exchanges?ids=${a.exchangeId}`}
              className="mt-0.5 shrink-0 font-mono text-xs text-crimson-700 hover:underline"
            >
              {a.exchangeId.slice(0, 8)}
            </Link>
            <span className="w-20 shrink-0 text-ink-400" title={formatDateTime(a.failedOn)}>
              {timeAgo(a.failedOn)}
            </span>
            {a.retryPending ? (
              <Badge tone="ok">Retry due</Badge>
            ) : a.blockedReason ? (
              <Badge tone="warn">Stopped</Badge>
            ) : null}
            <span className="min-w-0 flex-1 truncate text-ink-600" title={a.error}>
              {a.blockedReason ?? a.error}
            </span>
          </li>
        ))}
      </ol>
      {total > attempts.length && (
        <p className="text-[13px] text-ink-500">
          Showing the {attempts.length} most recent of {total} failures.{" "}
          <Link
            to={`/exchanges?integrationId=${row.integrationId}&status=failed`}
            className="font-medium text-crimson-700 hover:underline"
          >
            See them all
          </Link>
        </p>
      )}
    </div>
  );
}

/** Sets where one pair's alert goes, overriding the group and the policy behind it. */
function OverrideDialog({
  policyId,
  row,
  onClose,
}: {
  policyId: number;
  row: RetryUsageRow;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  // Seeded from what this pair currently sends, not from an empty form: an override replaces
  // the level above rather than merging with it, so starting blank would quietly drop the very
  // settings the alert needs to arrive.
  const [value, setValue] = useState({
    alertMode: row.override.alertMode,
    alertHandlerId: row.override.alertHandlerId ?? row.resolvedHandlerId,
    alertHandlerProperties:
      Object.keys(row.override.alertHandlerProperties).length > 0
        ? row.override.alertHandlerProperties
        : row.resolvedHandlerProperties,
  });

  const save = useMutation({
    mutationFn: () => api.saveRetryAlertOverride(policyId, { ...value, integrationId: row.integrationId, groupId: row.groupId }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["retry-usage"] });
      onClose();
    },
  });

  return (
    <Dialog title={`${row.integrationName} — ${row.groupName}`} onClose={onClose} wide>
      <div className="space-y-4">
        <p className="text-[13px] text-ink-500">
          Where this one integration's alert goes when this group's budget runs out. The most
          specific of the three levels — it wins over both the group and the policy.
        </p>
        <AlertRouting
          value={value}
          onChange={setValue}
          inherited={
            row.resolvedHandlerId ? (
              <>
                Sends through <code className="font-mono">{row.resolvedHandlerId}</code>, set by{" "}
                {row.resolvedFrom ? LEVEL_WORD[row.resolvedFrom] : "a level above"}.
              </>
            ) : row.silencedAt ? (
              `Silenced by ${LEVEL_WORD[row.silencedAt]}, so nothing is sent.`
            ) : (
              "No level above sends anything, so nothing is sent."
            )
          }
        />
        <FormError>{save.error?.message}</FormError>
        <div className="flex justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button
            variant="primary"
            busy={save.isPending}
            disabled={value.alertMode === "Send" && !value.alertHandlerId}
            onClick={() => save.mutate()}
          >
            Save routing
          </Button>
        </div>
      </div>
    </Dialog>
  );
}

export function UsagePanel({
  policyId,
  integrations,
  canEdit,
}: {
  policyId: number;
  /** Used to explain an empty report — the integrations are still following the policy. */
  integrations: IntegrationSetupRef[];
  canEdit: boolean;
}) {
  const queryClient = useQueryClient();
  const [filter, setFilter] = useState<FilterKey>("attention");
  const [open, setOpen] = useState<string | null>(null);
  const [overriding, setOverriding] = useState<RetryUsageRow | null>(null);
  const [resetting, setResetting] = useState<RetryUsageRow | "all" | null>(null);

  const usage = useQuery({ queryKey: ["retry-usage", policyId], queryFn: () => api.getRetryUsage(policyId) });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["retry-usage"] });

  if (usage.isPending) return <LoadingBlock label="Loading budget usage…" />;
  if (usage.isError)
    return (
      <Panel title="Budget usage">
        <FormError>{usage.error.message}</FormError>
      </Panel>
    );

  const rows = usage.data;

  if (rows.length === 0)
    return (
      <Panel
        title="Budget usage"
        description="What each budget has spent, and whether anyone was told when one ran out."
      >
        <p className="text-sm text-ink-500">
          {integrations.length === 0
            ? "No integration uses this policy yet."
            : "No group in this policy sets a total budget, so there is nothing to spend and nothing that can run out."}
        </p>
      </Panel>
    );

  const counts = Object.fromEntries(
    FILTERS.map((f) => [f.key, rows.filter(f.match).length]),
  ) as Record<FilterKey, number>;
  const active = FILTERS.find((f) => f.key === filter)!;
  const shown = rows.filter(active.match);
  const spent = rows.filter((r) => r.exhausted);

  return (
    <Panel
      title="Budget usage"
      description="What each budget has spent, and whether anyone was told when one ran out. Changes here take effect immediately — they are not part of the save bar."
      action={
        canEdit && spent.length > 0 ? (
          <Button size="sm" onClick={() => setResetting("all")}>
            <RotateCcw className="size-3.5" /> Reset {spent.length} exhausted
          </Button>
        ) : undefined
      }
    >
      <div role="tablist" aria-label="Filter budgets" className="mb-2 flex flex-wrap gap-1.5">
        {FILTERS.map((f) => {
          const on = f.key === filter;
          return (
            <button
              key={f.key}
              role="tab"
              aria-selected={on}
              onClick={() => setFilter(f.key)}
              className={`rounded-lg px-2.5 py-1 text-[13px] font-medium transition-colors ${
                on ? "bg-ink-900 text-white" : "bg-ink-50 text-ink-600 hover:bg-ink-100 hover:text-ink-900"
              }`}
            >
              {f.label} <span className={on ? "text-white/60" : "text-ink-400"}>{counts[f.key]}</span>
            </button>
          );
        })}
      </div>
      <p className="mb-2 text-[13px] text-ink-500">{active.blurb}</p>

      {shown.length === 0 ? (
        <p className="text-sm text-ink-500">Nothing here — which is the good outcome.</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-200 text-left text-sm">
            <thead>
              <tr className="border-b border-ink-100 text-[11px] font-medium tracking-wide text-ink-400 uppercase">
                <th className="w-px px-2 py-1.5" />
                <th className="max-w-0 px-3 py-1.5">Integration</th>
                <th className="max-w-0 px-3 py-1.5">Group</th>
                <th className="w-px px-3 py-1.5 whitespace-nowrap">Used</th>
                <th className="w-px px-3 py-1.5 whitespace-nowrap">Last failure</th>
                <th className="max-w-0 px-3 py-1.5">Alert goes to</th>
                <th className="w-px px-3 py-1.5 whitespace-nowrap">Alerted</th>
                <th className="w-px px-3 py-1.5" />
              </tr>
            </thead>
            <tbody>
              {shown.map((r) => {
                const key = `${r.integrationId}:${r.groupId}`;
                const isOpen = open === key;
                return [
                  <tr key={key} className="border-b border-ink-50">
                    <td className="px-2 py-1.5">
                      <button
                        onClick={() => setOpen(isOpen ? null : key)}
                        aria-expanded={isOpen}
                        aria-label={`Failures for ${r.integrationName} in ${r.groupName}`}
                        className="rounded-md p-1 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
                      >
                        {isOpen ? <ChevronDown className="size-3.5" /> : <ChevronRight className="size-3.5" />}
                      </button>
                    </td>
                    <td className="max-w-0 px-3 py-1.5">
                      <Link
                        to={`/subscriptions/${r.integrationId}`}
                        className="block truncate font-medium text-ink-900 hover:text-crimson-700"
                      >
                        {r.integrationName}
                      </Link>
                    </td>
                    <td className="max-w-0 px-3 py-1.5">
                      <span className="block truncate text-[13px] text-ink-600">{r.groupName}</span>
                    </td>
                    <td className="px-3 py-1.5 whitespace-nowrap">
                      <span className="flex items-center gap-1.5">
                        <span className="font-mono text-xs tabular-nums text-ink-700">
                          {r.used} / {r.total}
                        </span>
                        {r.exhausted && <Badge tone="danger">Exhausted</Badge>}
                      </span>
                    </td>
                    <td className="px-3 py-1.5 whitespace-nowrap text-[13px] text-ink-500">
                      {r.lastAttemptOn ? (
                        <span title={formatDateTime(r.lastAttemptOn)}>{timeAgo(r.lastAttemptOn)}</span>
                      ) : (
                        <span className="italic text-ink-400">never failed</span>
                      )}
                    </td>
                    <td className="max-w-0 px-3 py-1.5">
                      <AlertCell row={r} />
                    </td>
                    <td className="px-3 py-1.5 whitespace-nowrap">
                      <AlertedCell row={r} />
                    </td>
                    <td className="px-3 py-1.5 text-right whitespace-nowrap">
                      {canEdit && (
                        <span className="flex justify-end gap-1">
                          {r.lastAttemptOn && (
                            <Button size="sm" onClick={() => setResetting(r)}>
                              Reset
                            </Button>
                          )}
                          <Button size="sm" onClick={() => setOverriding(r)}>
                            Route alert
                          </Button>
                        </span>
                      )}
                    </td>
                  </tr>,
                  isOpen && (
                    <tr key={`${key}:open`} className="border-b border-ink-50 bg-ink-50/50">
                      {/*
                        max-w-0 stops this cell reporting an intrinsic width, the same trick the
                        shared Table uses. Without it a single unwrapped stack trace widened the
                        whole table, and every column after Integration was pushed out of view —
                        opening one row hid the data in all the others.
                      */}
                      <td colSpan={8} className="max-w-0 px-4 py-3">
                        <Attempts policyId={policyId} row={r} />
                      </td>
                    </tr>
                  ),
                ];
              })}
            </tbody>
          </table>
        </div>
      )}

      {overriding && (
        <OverrideDialog policyId={policyId} row={overriding} onClose={() => setOverriding(null)} />
      )}

      {resetting && (
        <ConfirmDialog
          title={resetting === "all" ? "Reset every exhausted budget?" : "Reset this budget?"}
          body={
            resetting === "all" ? (
              <>
                {spent.length} {spent.length === 1 ? "budget starts" : "budgets start"} again from zero,
                and {spent.length === 1 ? "its" : "their"} integrations begin retrying immediately. If
                the downstream is still down, they will spend it again.
              </>
            ) : (
              <>
                <strong className="font-medium text-ink-800">{resetting.integrationName}</strong> starts
                again from zero in <strong className="font-medium text-ink-800">{resetting.groupName}</strong>,
                and retries resume immediately.
              </>
            )
          }
          confirmLabel={resetting === "all" ? "Reset them all" : "Reset budget"}
          onConfirm={async () => {
            // No pair means every pair — which is why the sweep is scoped to what has actually
            // run out rather than sent as one policy-wide reset: a budget partway through is
            // spending it for a reason, and handing that back is not what was asked for.
            if (resetting === "all")
              await Promise.all(
                spent.map((r) =>
                  api.resetRetryUsage(policyId, { integrationId: r.integrationId, groupId: r.groupId }),
                ),
              );
            else
              await api.resetRetryUsage(policyId, {
                integrationId: resetting.integrationId,
                groupId: resetting.groupId,
              });
            await invalidate();
          }}
          onClose={() => setResetting(null)}
        />
      )}

      {counts.attention > 0 && filter !== "attention" && (
        <p className="mt-2 flex items-center gap-1.5 text-[13px] text-warn-700">
          <AlertTriangle className="size-3.5" />
          {counts.attention} {counts.attention === 1 ? "pair needs" : "pairs need"} attention.
        </p>
      )}
    </Panel>
  );
}
