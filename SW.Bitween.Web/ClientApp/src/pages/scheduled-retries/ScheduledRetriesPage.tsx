import { useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Play, X } from "lucide-react";
import { api, type ScheduledRetryQuery } from "../../api";
import { Can } from "../../auth/guards";
import { useSession } from "../../auth/SessionContext";
import { PageHeader } from "../../components/layout/PageHeader";
import { Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { TextInput } from "../../components/ui/forms";
import { SearchSelect } from "../../components/ui/SearchSelect";
import { ConfirmDialog } from "../../components/ui/overlays";
import { Table } from "../../components/ui/Table";
import { useSubscriptionsCache } from "../../components/config/shared";
import { formatDateTime, timeAgo, timeUntil } from "../../lib/dates";
import { PromotedProps } from "../../components/config/shared";

const PAGE_SIZE = 25;

const readQuery = (sp: URLSearchParams): ScheduledRetryQuery => ({
  subscriptionId: sp.get("subscriptionId") ? Number(sp.get("subscriptionId")) : undefined,
  informationTypeId: sp.get("informationTypeId") ? Number(sp.get("informationTypeId")) : undefined,
  exception: sp.get("exception") ?? undefined,
  offset: sp.get("offset") ? Number(sp.get("offset")) : 0,
  limit: PAGE_SIZE,
});

/**
 * Failed exchanges whose retry policy queued an automatic re-run. Rows are
 * ordered soonest-first; "Run now" jumps the queue.
 */
export function ScheduledRetriesPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const query = useMemo(() => readQuery(searchParams), [searchParams]);
  const [runNowId, setRunNowId] = useState<string | null>(null);
  const queryClient = useQueryClient();
  const { can } = useSession();

  const { data, isLoading } = useQuery({
    queryKey: ["scheduled-retries", searchParams.toString()],
    queryFn: () => api.searchScheduledRetries(query),
    refetchInterval: 30_000,
    placeholderData: keepPreviousData,
  });

  const subscriptions = useSubscriptionsCache().data ?? [];
  const infoTypes =
    useQuery({ queryKey: ["information-types"], queryFn: () => api.listInformationTypes() }).data ?? [];

  const setParam = (key: string, value: string | null, resetOffset = true) => {
    const next = new URLSearchParams(searchParams);
    if (value === null || value === "") next.delete(key);
    else next.set(key, value);
    if (resetOffset) next.delete("offset");
    setSearchParams(next, { replace: true });
  };

  const activeFilterCount = ["subscriptionId", "informationTypeId", "exception"].filter((k) =>
    searchParams.has(k),
  ).length;

  const runNow = useMutation({
    mutationFn: (id: string) => api.runScheduledRetryNow(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["scheduled-retries"] });
      void queryClient.invalidateQueries({ queryKey: ["exchanges"] });
    },
  });

  const rows = data?.result ?? [];
  const total = data?.total ?? 0;

  return (
    <div>
      <PageHeader
        title="Scheduled retries"
        description="Failed exchanges the retry policies will automatically re-run — soonest first."
        help={{
          title: "How scheduled retries work",
          body: (
            <>
              When an exchange fails, its subscription's{" "}
              <Link to="/retry-policies" className="font-medium text-crimson-700 hover:underline">
                retry policy
              </Link>{" "}
              decides whether — and when — to try again. The retry job re-runs each entry at its
              scheduled time; "Run now" executes one immediately instead of waiting.
            </>
          ),
        }}
      />

      {/* — filters — */}
      <div className="mb-4 grid grid-cols-2 gap-2 md:grid-cols-4">
        <SearchSelect
          aria-label="Filter by subscription"
          size="sm"
          clearLabel="Any subscription"
          value={query.subscriptionId?.toString() ?? ""}
          onChange={(v) => setParam("subscriptionId", v || null)}
          options={subscriptions.map((i) => ({ value: String(i.id), label: i.name }))}
        />
        <SearchSelect
          aria-label="Filter by information type"
          size="sm"
          clearLabel="Any information type"
          value={query.informationTypeId?.toString() ?? ""}
          onChange={(v) => setParam("informationTypeId", v || null)}
          options={infoTypes.map((t) => ({ value: String(t.id), label: t.name, code: t.code }))}
        />
        <TextInput
          aria-label="Filter by exception text"
          className="!h-8 text-[13px]"
          placeholder="Exception contains…"
          defaultValue={query.exception ?? ""}
          key={`exc-${query.exception ?? ""}`}
          onBlur={(e) => e.target.value !== (query.exception ?? "") && setParam("exception", e.target.value || null)}
          onKeyDown={(e) => e.key === "Enter" && setParam("exception", e.currentTarget.value || null)}
        />
        {activeFilterCount > 0 && (
          <button
            onClick={() => setSearchParams(new URLSearchParams(), { replace: true })}
            className="inline-flex h-8 items-center gap-1 justify-self-start rounded-lg px-2 text-[13px] font-medium text-crimson-700 hover:bg-crimson-50"
          >
            <X className="size-3.5" aria-hidden />
            Clear filters
          </button>
        )}
      </div>

      {isLoading ? (
        <LoadingBlock />
      ) : rows.length === 0 ? (
        <EmptyState title="Nothing waiting to retry">
          {activeFilterCount > 0
            ? "No pending auto-retries match these filters."
            : "When a retry policy schedules an automatic re-run for a failed exchange, it appears here."}
        </EmptyState>
      ) : (
        <Table
          rows={rows}
          rowKey={(r) => r.id}
          minWidth="min-w-260"
          columns={[
            {
              header: "Runs",
              className: "whitespace-nowrap",
              cell: (r) => (
                <>
                  <span className="font-medium text-ink-800">{timeUntil(r.on)}</span>
                  <span className="block text-xs text-ink-400">{formatDateTime(r.on)}</span>
                </>
              ),
            },
            {
              // Same identity rule as the Exchanges list: what it carries first,
              // the id only as a link out.
              header: "Properties",
              cell: (r) => <PromotedProps properties={r.promotedProperties} />,
            },
            {
              header: "Information type",
              cell: (r) => (
                <Link
                  to={`/information-types/${r.informationTypeId}`}
                  className="font-mono text-xs text-ink-600 hover:text-crimson-700 hover:underline"
                >
                  {r.informationTypeCode}
                </Link>
              ),
            },
            {
              header: "Subscription",
              cell: (r) =>
                r.subscriptionName ? (
                  <Link
                    to={`/subscriptions/${r.subscriptionId}`}
                    className="text-[13px] font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                  >
                    {r.subscriptionName}
                  </Link>
                ) : (
                  <span className="text-ink-400">—</span>
                ),
            },
            {
              header: "Failed",
              className: "whitespace-nowrap",
              cell: (r) => <span className="text-ink-500">{timeAgo(r.startedOn)}</span>,
            },
            {
              // The policy is what decided the "Runs" time, so it's the one thing the
              // exception alone can't explain. This is the subscription's policy as it
              // stands now — editing a policy doesn't reschedule retries it already queued.
              header: "Scheduled by",
              truncate: true,
              cell: (r) =>
                r.retryPolicyId !== null ? (
                  can("retry-policies.view") ? (
                    <Link
                      to={`/retry-policies/${r.retryPolicyId}`}
                      className="block truncate text-[13px] font-medium text-crimson-700 hover:underline"
                    >
                      {r.retryPolicyName}
                    </Link>
                  ) : (
                    <span className="block truncate text-[13px] font-medium text-ink-700">{r.retryPolicyName}</span>
                  )
                ) : r.subscriptionId !== null ? (
                  <Link
                    to={`/subscriptions/${r.subscriptionId}`}
                    title="The retry policy set on its subscription"
                    className="block truncate text-[13px] text-crimson-700 hover:underline"
                  >
                    policy on {r.subscriptionName ?? "its subscription"}
                  </Link>
                ) : (
                  <span className="text-[13px] text-ink-400">Policy since removed</span>
                ),
            },
            {
              header: "Exception",
              truncate: true,
              cell: (r) =>
                r.exception ? (
                  <span className="block truncate font-mono text-[11px] text-danger-700" title={r.exception}>
                    {r.exception}
                  </span>
                ) : (
                  <span className="text-ink-400">—</span>
                ),
            },
            {
              header: "",
              align: "right",
              // Matches what runNow itself enforces: the retry operates on an exchange.
              cell: (r) => (
                <span className="flex items-center justify-end gap-2">
                  <Link
                    to={`/exchanges?ids=${encodeURIComponent(r.id)}`}
                    title={`Open ${r.id} in Exchanges`}
                    className="text-xs font-medium text-ink-500 hover:text-crimson-700 hover:underline"
                  >
                    View exchange
                  </Link>
                  <Can permission="exchanges.operate">
                    <Button size="sm" onClick={() => setRunNowId(r.id)}>
                      <Play className="size-3.5" aria-hidden />
                      Run now
                    </Button>
                  </Can>
                </span>
              ),
            },
          ]}
          footer={
            <div className="flex items-center justify-between border-t border-ink-100 px-4 py-2.5 text-[13px] text-ink-500">
              <span>
                Showing {query.offset + 1}–{Math.min(query.offset + PAGE_SIZE, total)} of {total}
              </span>
              <span className="flex gap-1.5">
                <Button
                  size="sm"
                  disabled={query.offset === 0}
                  onClick={() => setParam("offset", String(Math.max(0, query.offset - PAGE_SIZE)), false)}
                >
                  Previous
                </Button>
                <Button
                  size="sm"
                  disabled={query.offset + PAGE_SIZE >= total}
                  onClick={() => setParam("offset", String(query.offset + PAGE_SIZE), false)}
                >
                  Next
                </Button>
              </span>
            </div>
          }
        />
      )}

      {runNowId && (
        <ConfirmDialog
          title="Run this retry now?"
          body={`The scheduled auto-retry for ${runNowId} runs immediately instead of waiting for its slot.`}
          confirmLabel="Run now"
          onConfirm={() => runNow.mutateAsync(runNowId)}
          onClose={() => setRunNowId(null)}
        />
      )}
    </div>
  );
}
