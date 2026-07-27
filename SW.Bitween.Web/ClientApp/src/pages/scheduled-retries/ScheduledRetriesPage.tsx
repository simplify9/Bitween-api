import { Fragment, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronDown, ChevronRight, Play, X } from "lucide-react";
import { api, type ScheduledRetryQuery } from "../../api";
import { Can } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { TextInput } from "../../components/ui/forms";
import { SearchSelect } from "../../components/ui/SearchSelect";
import { ConfirmDialog } from "../../components/ui/overlays";
import { useIntegrationsCache } from "../../components/config/shared";
import { formatDateTime, timeAgo, timeUntil } from "../../lib/dates";
import { XchangeId } from "../exchanges/shared";

const PAGE_SIZE = 25;

const readQuery = (sp: URLSearchParams): ScheduledRetryQuery => ({
  integrationId: sp.get("integrationId") ? Number(sp.get("integrationId")) : undefined,
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
  const [open, setOpen] = useState<Set<string>>(new Set());
  const [runNowId, setRunNowId] = useState<string | null>(null);
  const queryClient = useQueryClient();

  const { data, isLoading } = useQuery({
    queryKey: ["scheduled-retries", searchParams.toString()],
    queryFn: () => api.searchScheduledRetries(query),
    refetchInterval: 30_000,
    placeholderData: keepPreviousData,
  });

  const integrations = useIntegrationsCache().data ?? [];
  const infoTypes =
    useQuery({ queryKey: ["information-types"], queryFn: () => api.listInformationTypes() }).data ?? [];

  const setParam = (key: string, value: string | null, resetOffset = true) => {
    const next = new URLSearchParams(searchParams);
    if (value === null || value === "") next.delete(key);
    else next.set(key, value);
    if (resetOffset) next.delete("offset");
    setSearchParams(next, { replace: true });
  };

  const activeFilterCount = ["integrationId", "informationTypeId", "exception"].filter((k) =>
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

  const toggleOpen = (id: string) =>
    setOpen((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  return (
    <div>
      <PageHeader
        title="Scheduled retries"
        description="Failed exchanges the retry policies will automatically re-run — soonest first."
        help={{
          title: "How scheduled retries work",
          body: (
            <>
              When an exchange fails, its integration's{" "}
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
          aria-label="Filter by integration"
          size="sm"
          clearLabel="Any integration"
          value={query.integrationId?.toString() ?? ""}
          onChange={(v) => setParam("integrationId", v || null)}
          options={integrations.map((i) => ({ value: String(i.id), label: i.name }))}
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
        <div className="overflow-x-auto rounded-xl border border-ink-200 bg-white">
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="border-b border-ink-100 text-[11px] font-medium tracking-wide text-ink-400 uppercase">
                <th className="py-2.5 pl-4">Runs</th>
                <th className="px-3 py-2.5">Exchange</th>
                <th className="px-3 py-2.5">Flow</th>
                <th className="px-3 py-2.5">Failed</th>
                <th className="px-3 py-2.5 text-right">
                  <span className="sr-only">Actions</span>
                </th>
                <th className="w-8 px-2 py-2.5" />
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <Fragment key={r.id}>
                  <tr
                    onClick={() => toggleOpen(r.id)}
                    className="cursor-pointer border-b border-ink-50 transition-colors last:border-0 hover:bg-ink-50/60"
                  >
                    <td className="py-2.5 pl-4 whitespace-nowrap">
                      <span className="font-medium text-ink-800">{timeUntil(r.on)}</span>
                      <span className="block text-xs text-ink-400">{formatDateTime(r.on)}</span>
                    </td>
                    <td className="px-3 py-2.5" onClick={(e) => e.stopPropagation()}>
                      <span className="flex items-center gap-1.5">
                        <XchangeId id={r.id} />
                        <Link
                          to={`/exchanges?ids=${encodeURIComponent(r.id)}`}
                          title="Open in Exchanges"
                          className="text-xs font-medium text-ink-500 hover:text-crimson-700 hover:underline"
                        >
                          view
                        </Link>
                      </span>
                    </td>
                    <td className="px-3 py-2.5" onClick={(e) => e.stopPropagation()}>
                      <span className="flex flex-wrap items-center gap-1 text-[13px]">
                        <Link
                          to={`/information-types/${r.informationTypeId}`}
                          className="font-mono text-xs text-ink-600 hover:text-crimson-700 hover:underline"
                        >
                          {r.informationTypeCode}
                        </Link>
                        {r.integrationName && (
                          <>
                            <span className="text-ink-300">→</span>
                            <Link
                              to={`/subscriptions/${r.integrationId}`}
                              className="font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                            >
                              {r.integrationName}
                            </Link>
                          </>
                        )}
                      </span>
                    </td>
                    <td className="px-3 py-2.5 whitespace-nowrap text-ink-500">{timeAgo(r.startedOn)}</td>
                    <td className="px-3 py-2.5 text-right" onClick={(e) => e.stopPropagation()}>
                      {/* Matches what runNow itself enforces: the retry operates on an exchange. */}
                      <Can permission="exchanges.operate">
                        <Button size="sm" onClick={() => setRunNowId(r.id)}>
                          <Play className="size-3.5" aria-hidden />
                          Run now
                        </Button>
                      </Can>
                    </td>
                    <td className="px-2 py-2.5 text-ink-400">
                      {open.has(r.id) ? <ChevronDown className="size-4" /> : <ChevronRight className="size-4" />}
                    </td>
                  </tr>
                  {open.has(r.id) && (
                    <tr className="border-b border-ink-50 last:border-0">
                      <td colSpan={6} className="bg-ink-50/40 px-4 py-3">
                        {r.exception ? (
                          <pre className="max-h-40 overflow-auto rounded-lg bg-crimson-50 px-3 py-2.5 font-mono text-[11px] leading-relaxed whitespace-pre-wrap text-crimson-800">
                            {r.exception}
                          </pre>
                        ) : (
                          <p className="text-sm text-ink-500">No exception was recorded.</p>
                        )}
                      </td>
                    </tr>
                  )}
                </Fragment>
              ))}
            </tbody>
          </table>

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
        </div>
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
