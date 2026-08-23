import { Fragment, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronDown, ChevronRight, Plus, RotateCcw, X } from "lucide-react";
import { api, type ExchangeQuery, type ExchangeStatus } from "../../api";
import { Can } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { Select, TextInput } from "../../components/ui/forms";
import { SearchSelect } from "../../components/ui/SearchSelect";
import { useIntegrationsCache } from "../../components/config/shared";
import { timeAgo, timeUntil, duration } from "../../lib/dates";
import { ExchangeDrawer } from "./ExchangeDrawer";
import { JourneyStrip, RetryDialog, STATUS_LABELS, StatusBadge } from "./shared";
import { PromotedProps } from "../../components/config/shared";

const PAGE_SIZE = 25;
const STATUSES: ExchangeStatus[] = ["processing", "success", "badResponse", "failed"];

const REFRESH_OPTIONS = [
  { value: "0", label: "Refresh: off" },
  { value: "5000", label: "Refresh: 5s" },
  { value: "15000", label: "Refresh: 15s" },
  { value: "60000", label: "Refresh: 1m" },
];

/** Everything except paging counts as "a filter" for the Clear affordance. */
const FILTER_KEYS = ["status", "integrationId", "partnerId", "informationTypeId", "ids", "correlationId", "property", "from", "to"] as const;

const readQuery = (sp: URLSearchParams): ExchangeQuery => ({
  status: (sp.get("status") as ExchangeStatus | null) ?? undefined,
  integrationId: sp.get("integrationId") ? Number(sp.get("integrationId")) : undefined,
  partnerId: sp.get("partnerId") ? Number(sp.get("partnerId")) : undefined,
  informationTypeId: sp.get("informationTypeId") ? Number(sp.get("informationTypeId")) : undefined,
  ids: sp.get("ids") ?? undefined,
  correlationId: sp.get("correlationId") ?? undefined,
  property: sp.get("property") ?? undefined,
  from: sp.get("from") ? new Date(sp.get("from")! + "T00:00:00").toISOString() : undefined,
  to: sp.get("to") ? new Date(sp.get("to")! + "T23:59:59").toISOString() : undefined,
  offset: sp.get("offset") ? Number(sp.get("offset")) : 0,
  limit: PAGE_SIZE,
});

export function ExchangesPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const query = useMemo(() => readQuery(searchParams), [searchParams]);
  const [refreshMs, setRefreshMs] = useState(15_000);
  const [open, setOpen] = useState<Set<string>>(new Set());
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [bulkConfirm, setBulkConfirm] = useState(false);
  const [bulkResult, setBulkResult] = useState<string | null>(null);
  const queryClient = useQueryClient();

  const { data, isLoading, dataUpdatedAt } = useQuery({
    queryKey: ["exchanges", searchParams.toString()],
    queryFn: () => api.searchExchanges(query),
    refetchInterval: refreshMs || false,
    placeholderData: keepPreviousData,
  });

  const integrations = useIntegrationsCache().data ?? [];
  const partners = useQuery({ queryKey: ["partners"], queryFn: () => api.listPartners() }).data ?? [];
  const infoTypes =
    useQuery({ queryKey: ["information-types"], queryFn: () => api.listInformationTypes() }).data ?? [];

  /** Set (or drop) one URL param; changing any filter resets paging. */
  const setParam = (key: string, value: string | null, resetOffset = true) => {
    const next = new URLSearchParams(searchParams);
    if (value === null || value === "") next.delete(key);
    else next.set(key, value);
    if (resetOffset) next.delete("offset");
    setSearchParams(next, { replace: true });
  };

  const activeFilterCount = FILTER_KEYS.filter((k) => searchParams.has(k)).length;

  const toggleOpen = (id: string) =>
    setOpen((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  const toggleSelected = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  const rows = data?.result ?? [];
  const total = data?.total ?? 0;
  const allOnPageSelected = rows.length > 0 && rows.every((r) => selected.has(r.id));

  const bulkRetry = useMutation({
    mutationFn: (reset: boolean) => api.bulkRetryExchanges([...selected], { reset }),
    onSuccess: ({ retried, skipped }) => {
      setBulkConfirm(false);
      setSelected(new Set());
      setBulkResult(
        `${retried} retr${retried === 1 ? "y" : "ies"} started${skipped > 0 ? `, ${skipped} skipped (auto-retry already scheduled)` : ""}.`,
      );
      void queryClient.invalidateQueries({ queryKey: ["exchanges"] });
    },
  });

  return (
    <div>
      <PageHeader
        title="Exchanges"
        description="Every message that flows through Bitween — expand a row to follow its journey through the pipeline."
        actions={
          <Can permission="exchanges.operate">
            <Link to="/exchanges/new">
              <Button variant="primary">
                <Plus className="size-4" aria-hidden />
                New exchange
              </Button>
            </Link>
          </Can>
        }
      />

      {/* — status pills + live refresh — */}
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <button
          onClick={() => setParam("status", null)}
          aria-pressed={!query.status}
          className={`rounded-full px-3 py-1.5 text-[13px] font-medium transition-colors ${
            !query.status
              ? "bg-ink-900 text-white"
              : "border border-ink-200 bg-white text-ink-600 hover:border-ink-300 hover:bg-ink-50"
          }`}
        >
          All
        </button>
        {STATUSES.map((s) => {
          const active = query.status === s;
          return (
            <button
              key={s}
              onClick={() => setParam("status", active ? null : s)}
              aria-pressed={active}
              className={`rounded-full px-3 py-1.5 text-[13px] font-medium transition-colors ${
                active
                  ? "bg-ink-900 text-white"
                  : "border border-ink-200 bg-white text-ink-600 hover:border-ink-300 hover:bg-ink-50"
              }`}
            >
              {STATUS_LABELS[s]}
            </button>
          );
        })}
        <span className="ml-auto flex items-center gap-2">
          {refreshMs > 0 && (
            <span
              className="inline-block size-1.5 animate-pulse rounded-full bg-ok-600"
              title={`Live — refreshed ${timeAgo(new Date(dataUpdatedAt).toISOString())}`}
            />
          )}
          <Select
            aria-label="Refresh interval"
            className="!h-8 !w-auto text-[13px]"
            value={String(refreshMs)}
            onChange={(e) => setRefreshMs(Number(e.target.value))}
            options={REFRESH_OPTIONS}
          />
        </span>
      </div>

      {/* — filters — */}
      <div className="mb-4 grid grid-cols-2 gap-2 md:grid-cols-3 xl:grid-cols-6">
        <SearchSelect
          aria-label="Filter by integration"
          size="sm"
          clearLabel="Any integration"
          value={query.integrationId?.toString() ?? ""}
          onChange={(v) => setParam("integrationId", v || null)}
          options={integrations.map((i) => ({ value: String(i.id), label: i.name }))}
        />
        <SearchSelect
          aria-label="Filter by partner"
          size="sm"
          clearLabel="Any partner"
          value={query.partnerId?.toString() ?? ""}
          onChange={(v) => setParam("partnerId", v || null)}
          options={partners.map((p) => ({ value: String(p.id), label: p.name }))}
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
          aria-label="Filter by exchange ids"
          className="!h-8 text-[13px]"
          placeholder="Ids (comma separated)"
          defaultValue={query.ids ?? ""}
          key={`ids-${query.ids ?? ""}`}
          onBlur={(e) => e.target.value !== (query.ids ?? "") && setParam("ids", e.target.value || null)}
          onKeyDown={(e) => e.key === "Enter" && setParam("ids", e.currentTarget.value || null)}
        />
        <TextInput
          aria-label="Filter by correlation id"
          className="!h-8 text-[13px]"
          placeholder="Correlation id"
          defaultValue={query.correlationId ?? ""}
          key={`cid-${query.correlationId ?? ""}`}
          onBlur={(e) =>
            e.target.value !== (query.correlationId ?? "") && setParam("correlationId", e.target.value || null)
          }
          onKeyDown={(e) => e.key === "Enter" && setParam("correlationId", e.currentTarget.value || null)}
        />
        <TextInput
          aria-label="Filter by promoted property"
          className="!h-8 text-[13px]"
          placeholder="Property (key or value)"
          defaultValue={query.property ?? ""}
          key={`prop-${query.property ?? ""}`}
          onBlur={(e) => e.target.value !== (query.property ?? "") && setParam("property", e.target.value || null)}
          onKeyDown={(e) => e.key === "Enter" && setParam("property", e.currentTarget.value || null)}
        />
        <label className="flex items-center gap-1.5 text-[13px] text-ink-500">
          From
          <TextInput
            type="date"
            aria-label="Started after"
            className="!h-8 text-[13px]"
            value={searchParams.get("from") ?? ""}
            onChange={(e) => setParam("from", e.target.value || null)}
          />
        </label>
        <label className="flex items-center gap-1.5 text-[13px] text-ink-500">
          To
          <TextInput
            type="date"
            aria-label="Started before"
            className="!h-8 text-[13px]"
            value={searchParams.get("to") ?? ""}
            onChange={(e) => setParam("to", e.target.value || null)}
          />
        </label>
        {activeFilterCount > 0 && (
          <button
            onClick={() => {
              const next = new URLSearchParams();
              setSearchParams(next, { replace: true });
            }}
            className="inline-flex h-8 items-center gap-1 justify-self-start rounded-lg px-2 text-[13px] font-medium text-crimson-700 hover:bg-crimson-50"
          >
            <X className="size-3.5" aria-hidden />
            Clear filters ({activeFilterCount})
          </button>
        )}
      </div>

      {bulkResult && (
        <p className="mb-3 rounded-lg bg-ok-100 px-3 py-2 text-sm text-ok-600">{bulkResult}</p>
      )}

      {/* — list — */}
      {isLoading ? (
        <LoadingBlock />
      ) : rows.length === 0 ? (
        <EmptyState title="No exchanges match">
          {activeFilterCount > 0
            ? "Try removing some filters — or widen the date range."
            : "Traffic will show up here as soon as an integration processes something."}
        </EmptyState>
      ) : (
        <div className="overflow-x-auto rounded-xl border border-ink-200 bg-white">
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="border-b border-ink-100 text-[11px] font-medium tracking-wide text-ink-400 uppercase">
                <th className="w-10 py-2.5 pl-4">
                  <Can permission="exchanges.operate">
                    <input
                      type="checkbox"
                      aria-label="Select all on this page"
                      className="size-3.5 cursor-pointer accent-crimson-600"
                      checked={allOnPageSelected}
                      onChange={() =>
                        setSelected((prev) => {
                          const next = new Set(prev);
                          if (allOnPageSelected) rows.forEach((r) => next.delete(r.id));
                          else rows.forEach((r) => next.add(r.id));
                          return next;
                        })
                      }
                    />
                  </Can>
                </th>
                <th className="py-1.5 pr-3">Properties</th>
                <th className="px-3 py-1.5">Status</th>
                <th className="px-3 py-1.5">Journey</th>
                <th className="px-3 py-1.5">Information type</th>
                <th className="px-3 py-1.5">Partner</th>
                <th className="px-3 py-1.5">Integration</th>
                <th className="px-3 py-1.5">Started</th>
                <th className="w-8 px-2 py-2.5" />
              </tr>
            </thead>
            <tbody>
              {rows.map((x) => (
                <Fragment key={x.id}>
                  <tr
                    onClick={() => toggleOpen(x.id)}
                    className="cursor-pointer border-b border-ink-50 transition-colors last:border-0 hover:bg-ink-50/60"
                  >
                    <td className="py-1.5 pl-4" onClick={(e) => e.stopPropagation()}>
                      <Can permission="exchanges.operate">
                        <input
                          type="checkbox"
                          aria-label={`Select ${x.id}`}
                          className="size-3.5 cursor-pointer accent-crimson-600"
                          checked={selected.has(x.id)}
                          onChange={() => toggleSelected(x.id)}
                        />
                      </Can>
                    </td>
                    <td className="py-1.5 pr-3">
                      <PromotedProps properties={x.promotedProperties} fallbackId={x.id} />
                    </td>
                    <td className="px-3 py-1.5">
                      {/* Status and the relationship markers read as one thought:
                          "failed, and a retry is already booked". */}
                      <span className="flex flex-wrap items-center gap-1">
                        <StatusBadge status={x.status} />
                        {x.retryFor && <Badge tone="neutral">Retry</Badge>}
                        {x.scheduledRetryOn && (
                          <Badge tone="warn">Auto-retry {timeUntil(x.scheduledRetryOn)}</Badge>
                        )}
                        {x.aggregationXchangeId && <Badge tone="neutral">Aggregated</Badge>}
                      </span>
                    </td>
                    <td className="px-3 py-1.5">
                      <JourneyStrip x={x} />
                    </td>
                    {/* One column per thing, so values line up down the page —
                        the old single "Flow" cell was a sentence you had to re-read
                        on every row.

                        Only the links swallow the click, never the cells: three
                        opted-out cells would leave most of a cursor-pointer row
                        doing nothing when clicked. */}
                    <td className="px-3 py-1.5">
                      <Link
                        to={`/information-types/${x.informationTypeId}`}
                        onClick={(e) => e.stopPropagation()}
                        className="font-mono text-xs text-ink-600 hover:text-crimson-700 hover:underline"
                      >
                        {x.informationTypeCode}
                      </Link>
                    </td>
                    <td className="px-3 py-1.5 text-[13px]">
                      {x.partnerName ? (
                        <Link
                          to={`/partners/${x.partnerId}`}
                          onClick={(e) => e.stopPropagation()}
                          className="text-ink-700 hover:text-crimson-700 hover:underline"
                        >
                          {x.partnerName}
                        </Link>
                      ) : (
                        <span className="text-ink-400">—</span>
                      )}
                    </td>
                    <td className="px-3 py-1.5 text-[13px]">
                      {x.integrationName ? (
                        <Link
                          to={`/subscriptions/${x.integrationId}`}
                          onClick={(e) => e.stopPropagation()}
                          className="font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                        >
                          {x.integrationName}
                        </Link>
                      ) : (
                        <span className="text-ink-400">—</span>
                      )}
                    </td>
                    <td className="px-3 py-1.5 whitespace-nowrap text-ink-500">
                      {timeAgo(x.startedOn)}
                      {x.finishedOn && (
                        <span className="text-xs text-ink-400"> · {duration(x.startedOn, x.finishedOn)}</span>
                      )}
                    </td>
                    <td className="px-2 py-1.5 text-ink-400">
                      {open.has(x.id) ? <ChevronDown className="size-4" /> : <ChevronRight className="size-4" />}
                    </td>
                  </tr>
                  {open.has(x.id) && (
                    <tr className="border-b border-ink-50 last:border-0">
                      <td colSpan={9} className="bg-ink-50/40 px-4 py-3">
                        <ExchangeDrawer x={x} />
                      </td>
                    </tr>
                  )}
                </Fragment>
              ))}
            </tbody>
          </table>

          {/* — paging — */}
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

      {/* — bulk action bar — */}
      {selected.size > 0 && (
        <div className="sticky bottom-4 mt-4 flex items-center justify-between rounded-xl border border-ink-200 bg-white px-4 py-2.5 shadow-lg">
          <span className="text-sm text-ink-700">
            <strong className="font-semibold">{selected.size}</strong> selected
          </span>
          <span className="flex gap-2">
            <Button size="sm" variant="ghost" onClick={() => setSelected(new Set())}>
              Clear
            </Button>
            <Button size="sm" variant="primary" onClick={() => setBulkConfirm(true)}>
              <RotateCcw className="size-3.5" aria-hidden />
              Retry selected…
            </Button>
          </span>
        </div>
      )}

      {bulkConfirm && (
        <RetryDialog
          count={selected.size}
          busy={bulkRetry.isPending}
          onConfirm={(reset) => bulkRetry.mutate(reset)}
          onClose={() => setBulkConfirm(false)}
        />
      )}
    </div>
  );
}
