import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowUp, Download, Pause, Play, Plus, RefreshCw, RotateCcw, TriangleAlert, X } from "lucide-react";
import {
  api,
  type BulkRetrySelection,
  type ExchangeStatus,
  type ExportFilesCheck,
} from "../../api";
import { Can } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { ConfirmDialog } from "../../components/ui/overlays";
import { openDialogs } from "../../components/ui/dialogStack";
import { useSubscriptionsCache } from "../../components/config/lookups";
import { timeAgo } from "../../lib/dates";
import { keys } from "../../api/queryKeys";
import { NONE } from "../../lib/none";
import { RetryDialog } from "./shared";
import { ExchangeList } from "./ExchangeList";
import { ExchangeDetailPanel } from "./ExchangeDetailPanel";
import { QueryBar, type FilterOptions } from "./QueryBar";
import { StatusTabs } from "./StatusTabs";
import { VolumeChart } from "./VolumeChart";
import {
  COUNT_CAP,
  PAGE_SIZE,
  activeFilterCount,
  filterSignature,
  hasTimeFilter,
  idsIn,
  readQuery,
  timeLabel,
  toLocalInput,
  viewSearch,
  withParams,
} from "./exchangeFilters";

/** The most exchanges one files export takes: `XchangeExportController.Limit` on the server. */
const EXPORT_LIMIT = 500;

const exchangesCount = (n: number) => (n === 1 ? "1 exchange" : `${n.toLocaleString()} exchanges`);

/** Why some of an export's exchanges come without files; said before it downloads. */
function filesGoneText({ count, withoutFiles, keptDays }: ExportFilesCheck): string {
  const kept = keptDays === null ? "" : ` (${keptDays} days)`;
  if (withoutFiles >= count)
    return count === 1
      ? `The selected exchange is older than storage keeps files${kept}, so there's nothing to export.`
      : `All ${exchangesCount(count)} selected are older than storage keeps files${kept}, so there's nothing to export.`;
  return `${withoutFiles.toLocaleString()} of the ${exchangesCount(count)} selected ${
    withoutFiles === 1 ? "is" : "are"
  } older than storage keeps files${kept}, so the zip won't have ${withoutFiles === 1 ? "its" : "their"} files.`;
}

const REFRESH_OPTIONS = [
  { value: "0", label: "Paused" },
  { value: "5000", label: "every 5s" },
  { value: "15000", label: "every 15s" },
  { value: "60000", label: "every 1m" },
];

/** Typing in a field, or a dialog on top: the page's single-key shortcuts stand aside. */
const shortcutsBlocked = (e: KeyboardEvent) => {
  if (e.metaKey || e.ctrlKey || e.altKey || openDialogs.length > 0) return true;
  const t = e.target as HTMLElement | null;
  return !!t?.closest?.("input, textarea, select, [contenteditable=true], [role=listbox], [role=menu]");
};

export function ExchangesPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const query = useMemo(() => readQuery(searchParams), [searchParams]);
  const [refreshMs, setRefreshMs] = useState(15_000);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  /**
   * "Select all matching" holds the filter rather than a list of ids, so a selection is not
   * limited to the 25 rows one page happened to load. Unticking a row in this mode records an
   * exclusion instead of removing an id.
   */
  const [allMatching, setAllMatching] = useState(false);
  const [excluded, setExcluded] = useState<Set<string>>(new Set());
  const [bulkConfirm, setBulkConfirm] = useState(false);
  /** Mirrors the confirm dialog's own choice, because the plan depends on it. */
  const [bulkReset, setBulkReset] = useState(false);
  const [bulkResult, setBulkResult] = useState<string | null>(null);
  const queryClient = useQueryClient();

  const apply = useCallback(
    (changes: Record<string, string | null>, opts?: { resetOffset?: boolean }) =>
      setSearchParams((prev) => withParams(prev, changes, opts), { replace: true }),
    [setSearchParams],
  );

  // A relative range ("last hour") is worked out each time the list is asked for, so a live list
  // keeps showing the last hour rather than the hour before the page was opened.
  const { data, isLoading, isFetching, isPlaceholderData, dataUpdatedAt, refetch } = useQuery({
    queryKey: keys.exchanges.search(viewSearch(searchParams) + `&offset=${query.offset}`),
    queryFn: () => api.searchExchanges(readQuery(searchParams)),
    refetchInterval: refreshMs || false,
    placeholderData: keepPreviousData,
  });
  // Loads someone is waiting on: a changed filter or page (the old rows stay up meanwhile), or the
  // Refresh button. Not the background ticks of live refresh, which would dim the list every 15s.
  const [refreshing, setRefreshing] = useState(false);
  const reloading = isPlaceholderData || refreshing;

  // --- counts and volume ---
  // With a time filter, one request answers both. Without one the list covers all time, which
  // the counts follow, while the chart shows the last 24 hours and says so - a histogram of
  // "all time" is one bar.
  const timed = hasTimeFilter(searchParams);
  const breakdownKey = viewSearch(withParams(searchParams, { status: null }));
  const breakdown = useQuery({
    queryKey: keys.exchanges.breakdown(breakdownKey),
    queryFn: () => api.getExchangeBreakdown(readQuery(searchParams)),
    refetchInterval: refreshMs || false,
    placeholderData: keepPreviousData,
    retry: false,
  });
  const chartParams = timed ? null : withParams(searchParams, { range: "24h", status: null });
  const chartBreakdown = useQuery({
    queryKey: keys.exchanges.breakdown(chartParams ? viewSearch(chartParams) : ""),
    queryFn: () => api.getExchangeBreakdown(readQuery(chartParams!)),
    enabled: !!chartParams,
    refetchInterval: refreshMs || false,
    placeholderData: keepPreviousData,
    retry: false,
  });
  const volume = timed ? breakdown.data : chartBreakdown.data;

  // Now, rather than at the next tick - or at all, paused. The counts and the chart come along so
  // they never disagree with the rows. The chart's query is off when the list is timed, and a
  // refetch would run it anyway, against no parameters.
  const refresh = () => {
    setRefreshing(true);
    void Promise.all([refetch(), breakdown.refetch(), chartParams ? chartBreakdown.refetch() : null]).finally(() =>
      setRefreshing(false),
    );
  };
  const rangeEndsNow = !searchParams.get("to") && searchParams.get("range") !== "yesterday";

  const subscriptions = useSubscriptionsCache().data ?? [];
  const partners = useQuery({ queryKey: keys.partners.list, queryFn: () => api.listPartners() }).data ?? [];
  const infoTypes =
    useQuery({ queryKey: keys.informationTypes.list, queryFn: () => api.listInformationTypes() }).data ?? NONE;

  /**
   * Every promoted key any information type declares, with the types that declare it.
   * Read off the list already fetched for the information-type filter, so offering the
   * keys costs nothing - and picking from real names beats remembering how one was spelled.
   *
   * Narrowed to the picked information type when there is one: a short list of its own keys
   * beats the whole catalogue with a disambiguating line on every row.
   */
  const propertyKeyOptions = useMemo(() => {
    const scoped = query.informationTypeId ? infoTypes.filter((t) => t.id === query.informationTypeId) : infoTypes;
    const owners = new Map<string, { type: string; path: string }[]>();
    for (const t of scoped)
      for (const p of t.promotedProperties ?? []) {
        const carriers = owners.get(p.key) ?? [];
        const type = t.code ?? t.name;
        if (!carriers.some((c) => c.type === type)) carriers.push({ type, path: p.path });
        owners.set(p.key, carriers);
      }
    return [...owners.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([key, carriers]) => ({
        value: key,
        label: key,
        // A second line rather than text beside the name: these run long enough
        // ("InventoryTransactionPosted") that sharing one line left the name with no room at
        // all. Dropped once a type is picked above - naming it again on every row says nothing.
        sublabel: query.informationTypeId ? undefined : carriers.map((c) => c.type).join(", "),
        // What the key actually reads out of the payload. The thing you want when a filter
        // comes back empty and you can't tell whether the key or the value is wrong.
        title: carriers.map((c) => `${c.type}: ${c.path}`).join("\n"),
      }));
  }, [infoTypes, query.informationTypeId]);

  const filterOptions: FilterOptions = {
    subscriptions: subscriptions.map((i) => ({ value: String(i.id), label: i.name })),
    partners: partners.map((p) => ({ value: String(p.id), label: p.name })),
    informationTypes: infoTypes.map((t) => ({ value: String(t.id), label: t.name, code: t.code })),
    propertyKeys: propertyKeyOptions,
  };
  const lookups = {
    subscriptionName: (id: number) => subscriptions.find((s) => s.id === id)?.name,
    partnerName: (id: number) => partners.find((p) => p.id === id)?.name,
    informationTypeName: (id: number) => infoTypes.find((t) => t.id === id)?.name,
  };

  const filterCount = activeFilterCount(searchParams);
  const rows = data?.result ?? [];
  const total = data?.total ?? 0;
  const totalIsCapped = total > COUNT_CAP;

  // --- the open exchange ---
  // In the URL, so a link can open one. A link to a single exchange (`?ids=<id>`, as the
  // dashboard and the scheduled retries list make) opens it straight away: that is what it was
  // followed for.
  const singleId = (() => {
    const ids = query.ids ? idsIn(query.ids) : null;
    return ids?.length === 1 ? ids[0] : null;
  })();
  // "-" is a closed panel that stays closed: without it, closing an exchange that a single-id
  // link opened would open it again on the next render.
  const openParam = searchParams.get("open");
  const openId =
    openParam === "-" ? null : (openParam ?? (singleId && rows.some((r) => r.id === singleId) ? singleId : null));
  const openOnPage = rows.find((r) => r.id === openId);
  // Opened from a link, or kept open while the page moved on: fetched on its own.
  const openElsewhere = useQuery({
    queryKey: keys.exchanges.search(`open=${openId}`),
    queryFn: () => api.searchExchanges({ ids: openId!, offset: 0, limit: 10 }),
    enabled: !!openId && !openOnPage && !isLoading,
  });
  const openRow = openOnPage ?? openElsewhere.data?.result.find((r) => r.id === openId) ?? null;
  const openIndex = openOnPage ? rows.indexOf(openOnPage) : -1;
  const setOpen = useCallback(
    (id: string | null) => apply({ open: id ?? (singleId ? "-" : null) }, { resetOffset: false }),
    [apply, singleId],
  );
  const toggleOpen = (id: string) => setOpen(openId === id ? null : id);

  // --- selection ---
  const toggle = (set: Set<string>, id: string) => {
    const next = new Set(set);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    return next;
  };
  const toggleSelected = (id: string) =>
    allMatching ? setExcluded((prev) => toggle(prev, id)) : setSelected((prev) => toggle(prev, id));
  const clearSelection = () => {
    setSelected(new Set());
    setExcluded(new Set());
    setAllMatching(false);
  };
  const isSelected = (id: string) => (allMatching ? !excluded.has(id) : selected.has(id));
  const allOnPageSelected = rows.length > 0 && rows.every((r) => isSelected(r.id));
  const selectedCount = allMatching ? Math.max(0, total - excluded.size) : selected.size;

  /** What the retry acts on: the ticked rows, or the filter itself minus the unticked ones. */
  const selection: BulkRetrySelection = allMatching ? { matching: query, excludeIds: [...excluded] } : { ids: [...selected] };

  /**
   * A selection means a set of exchanges, and changing the filters changes which set that is -
   * for "all matching" it would silently come to mean something else entirely. Paging is not a
   * filter, so ticking rows across pages still accumulates.
   */
  const filterKey = filterSignature(searchParams);
  useEffect(clearSelection, [filterKey]);

  // --- keyboard ---
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (shortcutsBlocked(e)) return;
      if (e.key === "Escape" && openId) {
        setOpen(null);
        return;
      }
      if (rows.length === 0) return;
      if (e.key === "j" || e.key === "k") {
        e.preventDefault();
        const at = openIndex < 0 ? (e.key === "j" ? 0 : rows.length - 1) : openIndex + (e.key === "j" ? 1 : -1);
        const next = rows[Math.max(0, Math.min(rows.length - 1, at))];
        setOpen(next.id);
        document.querySelector(`[data-row-id="${next.id}"]`)?.scrollIntoView?.({ block: "nearest" });
      } else if (e.key === "x" && openId) {
        e.preventDefault();
        toggleSelected(openId);
      }
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  });

  // --- while paused: how much has arrived since ---
  const newest = rows[0]?.startedOn;
  const watchNew = refreshMs === 0 && query.offset === 0 && !!newest;
  const since = useQuery({
    queryKey: keys.exchanges.newSince(viewSearch(searchParams) + newest),
    queryFn: () => {
      const after = new Date(new Date(newest!).getTime() + 1);
      const from = query.from && new Date(query.from) > after ? query.from : after.toISOString();
      return api.searchExchanges({ ...readQuery(searchParams), from, offset: 0, limit: 1 });
    },
    enabled: watchNew,
    refetchInterval: watchNew ? 15_000 : false,
  });
  const newCount = watchNew ? (since.data?.total ?? 0) : 0;

  // --- bulk retry ---
  /**
   * What the retry would do, asked for while the confirm is open. A selection is rarely just
   * itself - anything already retried hands over to its newest attempt - so this is shown
   * before anyone commits rather than reported afterwards.
   */
  const { data: plan, isFetching: planLoading } = useQuery({
    queryKey: keys.exchanges.bulkRetryPreview(JSON.stringify({ selection, reset: bulkReset })),
    queryFn: () => api.previewBulkRetry(selection, { reset: bulkReset }),
    enabled: bulkConfirm && selectedCount > 0,
    staleTime: 30_000,
  });

  const bulkRetry = useMutation({
    mutationFn: (reset: boolean) => api.bulkRetryExchanges(selection, { reset }),
    onSuccess: (done) => {
      setBulkConfirm(false);
      setBulkReset(false);
      clearSelection();
      setBulkResult(
        `${done.willRetry.toLocaleString()} retr${done.willRetry === 1 ? "y" : "ies"} started` +
          (done.substituted.length > 0 ? `, ${done.substituted.length} continuing an existing chain` : "") +
          (done.skipped.length > 0 ? `, ${done.skipped.length} skipped` : "") +
          ".",
      );
      void queryClient.invalidateQueries({ queryKey: keys.exchanges.all });
    },
  });

  // Asked first, so someone hears that some exchanges are past the files storage keeps before choosing to
  // download, rather than from a list inside the zip. Nothing missing: it downloads straight away.
  const [exportCheck, setExportCheck] = useState<ExportFilesCheck | null>(null);
  const [nothingToExport, setNothingToExport] = useState<string | null>(null);
  const checkExport = useMutation({
    mutationFn: () => api.checkExchangeFilesExport(selection),
    onMutate: () => setNothingToExport(null),
    onSuccess: (check) => {
      if (check.withoutFiles === 0) exportFiles.mutate();
      else if (check.withoutFiles >= check.count) setNothingToExport(filesGoneText(check));
      else setExportCheck(check);
    },
  });

  /** The zip comes back whole and is saved from memory: the endpoint needs the sign-in header, which a plain link can't send. */
  const exportFiles = useMutation({
    mutationFn: () => api.exportExchangeFiles(selection),
    onSuccess: ({ blob, fileName }) => {
      // In the document while clicked, and revoked a while later: Firefox and Safari ignore a click on a
      // detached link, and start the download after the click returns, so an address revoked at once fails it.
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = fileName;
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 60_000);
    },
  });

  const pickStatus = (status: ExchangeStatus | null) => apply({ status });
  const backAPage = () => apply({ offset: String(Math.max(0, query.offset - PAGE_SIZE)) }, { resetOffset: false });

  return (
    <div>
      <PageHeader
        title="Exchanges"
        description="Every message that flows through Bitween. Open one to follow its journey and read its documents."
        help={{
          title: "What's on this page?",
          body: (
            <>
              <p>
                Each row is one <strong>exchange</strong>: a document run through a subscription. Retrying
                one creates a new exchange in the same <strong>chain</strong>, so one piece of work can show
                up as several rows. <strong>Latest attempt only</strong> hides the attempts that have since
                been retried, leaving what is still to deal with.
              </p>
              <p>
                The status buttons count exchanges under every other filter. Counts stop at 10,000; narrow
                the filters for an exact number. Drag across the volume chart, or click a bar, to zoom the
                list to that stretch of time.
              </p>
              <p>
                Select rows, or everything matching the filter, to <strong>retry</strong> them in bulk or{" "}
                <strong>export their files</strong> as a zip. Exchanges older than storage keeps files come
                without them.
              </p>
            </>
          ),
        }}
        actions={
          <>
            <LiveControl
              refreshMs={refreshMs}
              onChange={setRefreshMs}
              updatedAt={dataUpdatedAt}
              onRefresh={refresh}
              fetching={isFetching}
            />
            <Can permission="exchanges.operate">
              <Link to="/exchanges/new">
                <Button variant="primary">
                  <Plus className="size-4" aria-hidden />
                  New exchange
                </Button>
              </Link>
            </Can>
          </>
        }
      />

      <div className="flex flex-col gap-3.5">
        <StatusTabs
          status={query.status}
          counts={breakdown.data?.counts}
          onPick={pickStatus}
          latest={!!query.latest}
          onToggleLatest={() => apply({ latest: query.latest ? null : "1" })}
        />

        <QueryBar searchParams={searchParams} apply={apply} options={filterOptions} lookups={lookups} />

        {volume && volume.buckets.length > 0 && (
          <VolumeChart
            buckets={volume.buckets}
            bucketMinutes={volume.bucketMinutes}
            caption={timed ? timeLabel(searchParams).toLowerCase() : "in the last 24 hours"}
            endsNow={timed ? rangeEndsNow : true}
            onSelect={(from, to) => apply({ range: null, from: toLocalInput(from), to: toLocalInput(to) })}
          />
        )}

        {bulkResult && (
          <p role="status" className="flex items-center justify-between gap-2 rounded-lg bg-ok-100 px-3 py-2 text-sm text-ok-600">
            {bulkResult}
            <button type="button" onClick={() => setBulkResult(null)} aria-label="Dismiss" className="rounded p-0.5 hover:bg-ok-200">
              <X className="size-3.5" aria-hidden />
            </button>
          </p>
        )}

        {newCount > 0 && (
          <button
            type="button"
            onClick={() => void queryClient.invalidateQueries({ queryKey: keys.exchanges.all })}
            className="inline-flex items-center gap-2 self-center rounded-full bg-ink-900 px-3.5 py-1.5 text-[13px] font-medium text-ink-50 hover:bg-ink-800"
          >
            <ArrowUp className="size-3.5" aria-hidden />
            {newCount > COUNT_CAP ? `${COUNT_CAP.toLocaleString()}+` : newCount.toLocaleString()} new exchange
            {newCount === 1 ? "" : "s"} since you paused. Show them
          </button>
        )}

        <div className="flex items-start gap-4">
          <div className="flex min-w-0 flex-1 flex-col gap-3">
            {isLoading ? (
              <LoadingBlock />
            ) : rows.length === 0 ? (
              /* An empty page and an empty search are different problems. This branch replaces the
                 table, paging footer and all, so a page past the end of the list would otherwise
                 leave nothing to click back to. Reachable two ways: a hand-typed ?offset=, and Next
                 past the count cap, where a last page that happens to be full still enables it. */
              <EmptyState
                title={query.offset > 0 ? "Nothing on this page" : "No exchanges match"}
                action={
                  query.offset > 0 ? (
                    <Button onClick={backAPage}>Back a page</Button>
                  ) : filterCount > 0 ? (
                    <span className="flex flex-wrap justify-center gap-2">
                      {timed && searchParams.get("range") !== "30d" && (
                        <Button size="sm" onClick={() => apply({ range: "30d", from: null, to: null })}>
                          Widen to the last 30 days
                        </Button>
                      )}
                      {query.status && (
                        <Button size="sm" onClick={() => apply({ status: null })}>
                          Any status
                        </Button>
                      )}
                      <Button size="sm" variant="ghost" onClick={() => setSearchParams(new URLSearchParams(), { replace: true })}>
                        Clear all filters
                      </Button>
                    </span>
                  ) : undefined
                }
              >
                {query.offset > 0
                  ? "The list ends before this page."
                  : filterCount > 0
                    ? "Try removing some filters - or widen the time range."
                    : "Traffic will show up here as soon as a subscription processes something."}
              </EmptyState>
            ) : (
              <div className="relative rounded-xl border border-ink-200 bg-white" aria-busy={reloading}>
                {reloading && (
                  <div className="absolute inset-x-0 top-0 z-10 h-0.5 animate-pulse bg-crimson-500" title="Loading…" />
                )}
                <div className={`transition-opacity ${reloading ? "opacity-50" : ""}`}>
                  <ExchangeList
                    rows={rows}
                    openId={openId}
                    onOpen={toggleOpen}
                    compact={!!openRow}
                    selection={{
                      isSelected,
                      toggle: toggleSelected,
                      allOnPage: allOnPageSelected,
                      toggleAllOnPage: () => {
                        // In "all matching" mode the box stands for the whole filter, so
                        // clearing it drops the whole selection rather than excluding 25 rows.
                        if (allMatching) return clearSelection();
                        setSelected((prev) => {
                          const next = new Set(prev);
                          if (allOnPageSelected) rows.forEach((r) => next.delete(r.id));
                          else rows.forEach((r) => next.add(r.id));
                          return next;
                        });
                      },
                    }}
                  />
                </div>

                {/* - paging - */}
                <div className="flex flex-wrap items-center justify-between gap-2 border-t border-ink-100 px-4 py-2.5 text-[13px] text-ink-500">
                  <span>
                    Showing {query.offset + 1}-{query.offset + rows.length} of{" "}
                    {totalIsCapped ? (
                      <span title={`More than ${COUNT_CAP.toLocaleString()} match - narrow the filters for an exact count`}>
                        {COUNT_CAP.toLocaleString()}+
                      </span>
                    ) : (
                      total.toLocaleString()
                    )}
                  </span>
                  <span className="flex items-center gap-3">
                    <span className="hidden text-xs text-ink-500 lg:inline">
                      <Kbd>J</Kbd> <Kbd>K</Kbd> move · <Kbd>X</Kbd> select · <Kbd>Esc</Kbd> close
                    </span>
                    <span className="flex gap-1.5">
                      <Button size="sm" disabled={query.offset === 0} onClick={backAPage}>
                        Previous
                      </Button>
                      <Button
                        size="sm"
                        /* Past the cap the total no longer says where the end is, so fall back to "was this
                           page full" - otherwise Next would dead-end at row 10,000. Below the cap the total
                           is exact, so keep using it and Next stops on the true last page. */
                        disabled={totalIsCapped ? rows.length < PAGE_SIZE : query.offset + PAGE_SIZE >= total}
                        onClick={() => apply({ offset: String(query.offset + PAGE_SIZE) }, { resetOffset: false })}
                      >
                        Next
                      </Button>
                    </span>
                  </span>
                </div>
              </div>
            )}

            {/* - bulk action bar - */}
            {selectedCount > 0 && (
              <div className="sticky bottom-4 z-20 space-y-2 rounded-xl bg-ink-950 px-4 py-2.5 text-ink-100 shadow-xl">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="text-sm">
                    <strong className="font-semibold text-white">
                      {totalIsCapped && allMatching ? `${COUNT_CAP.toLocaleString()}+` : selectedCount.toLocaleString()}
                    </strong>{" "}
                    selected
                    {allMatching && <span className="text-ink-300"> - everything this filter matches</span>}
                    {allMatching && excluded.size > 0 && <span className="text-ink-300">, {excluded.size} unticked</span>}
                  </span>
                  <span className="flex flex-wrap gap-2">
                    <button
                      type="button"
                      onClick={clearSelection}
                      className="h-8 rounded-lg px-3 text-[13px] text-ink-200 hover:bg-white/10"
                    >
                      Clear
                    </button>
                    <Button
                      size="sm"
                      className="!border-ink-700 !bg-ink-900 !text-ink-50 hover:!bg-ink-800 disabled:!text-ink-500"
                      busy={checkExport.isPending || exportFiles.isPending}
                      disabled={selectedCount > EXPORT_LIMIT}
                      onClick={() => checkExport.mutate()}
                      title={
                        selectedCount > EXPORT_LIMIT
                          ? `One export takes at most ${EXPORT_LIMIT} exchanges. Narrow the filter or tick fewer rows.`
                          : "Download the input, mapped and handled files of the selected exchanges as one zip, a folder per exchange"
                      }
                    >
                      <Download className="size-3.5" aria-hidden />
                      Export files
                    </Button>
                    <Can permission="exchanges.operate">
                      <Button size="sm" variant="primary" onClick={() => setBulkConfirm(true)}>
                        <RotateCcw className="size-3.5" aria-hidden />
                        Retry selected…
                      </Button>
                    </Can>
                  </span>
                </div>

                {(checkExport.error ?? exportFiles.error ?? nothingToExport) && (
                  <p role="alert" className="flex items-start gap-1 text-[13px] text-danger-300">
                    <TriangleAlert className="mt-0.5 size-3.5 shrink-0" aria-hidden />
                    <span>{(checkExport.error ?? exportFiles.error)?.message ?? nothingToExport}</span>
                  </p>
                )}

                {/* The whole page is ticked but there is more behind it - the one moment where
                    "select all matching" is what someone actually wants, so it is offered there
                    rather than living permanently in the toolbar. */}
                {!allMatching && allOnPageSelected && total > rows.length && (
                  <p className="text-[13px] text-ink-300">
                    Only the {rows.length} rows on this page.{" "}
                    <button
                      type="button"
                      onClick={() => {
                        setAllMatching(true);
                        setSelected(new Set());
                        setExcluded(new Set());
                      }}
                      className="font-medium text-white underline decoration-ink-400 underline-offset-3 hover:decoration-crimson-300"
                    >
                      Select all {totalIsCapped ? `${COUNT_CAP.toLocaleString()}+` : total.toLocaleString()} matching this filter
                    </button>
                  </p>
                )}
              </div>
            )}
          </div>

          {openRow && (
            <ExchangeDetailPanel
              x={openRow}
              onClose={() => setOpen(null)}
              onPrevious={openIndex > 0 ? () => setOpen(rows[openIndex - 1].id) : undefined}
              onNext={openIndex >= 0 && openIndex < rows.length - 1 ? () => setOpen(rows[openIndex + 1].id) : undefined}
            />
          )}
        </div>
      </div>

      {exportCheck && (
        <ConfirmDialog
          title="Some files are gone"
          body={filesGoneText(exportCheck)}
          confirmLabel={`Export the other ${exchangesCount(exportCheck.count - exportCheck.withoutFiles)}`}
          confirmVariant="primary"
          onConfirm={async () => {
            await exportFiles.mutateAsync();
          }}
          onClose={() => setExportCheck(null)}
        />
      )}

      {bulkConfirm && (
        <RetryDialog
          count={selectedCount}
          plan={plan ?? null}
          planLoading={planLoading}
          busy={bulkRetry.isPending}
          onConfirm={(reset) => bulkRetry.mutate(reset)}
          onResetChange={setBulkReset}
          onClose={() => {
            setBulkConfirm(false);
            // The dialog starts unticked each time it opens, so the mirrored copy has to as well.
            setBulkReset(false);
          }}
        />
      )}
    </div>
  );
}

function Kbd({ children }: { children: string }) {
  return <kbd className="rounded border border-ink-200 bg-ink-50 px-1 font-mono text-[11px] text-ink-600">{children}</kbd>;
}

/**
 * How often the list refreshes itself, and the pause. Paused, the rows stay where they are while
 * someone reads them, and the page counts what arrives instead of shifting it in underneath them.
 */
function LiveControl({
  refreshMs,
  onChange,
  updatedAt,
  onRefresh,
  fetching,
}: {
  refreshMs: number;
  onChange: (ms: number) => void;
  updatedAt: number;
  onRefresh: () => void;
  fetching: boolean;
}) {
  const [lastLive, setLastLive] = useState(15_000);
  const live = refreshMs > 0;
  return (
    <span className="inline-flex h-9.5 items-stretch overflow-hidden rounded-full border border-ink-200 bg-white">
      <label
        className="flex items-center gap-2 pr-1 pl-3 text-[13px] text-ink-700"
        title={live && updatedAt ? `Refreshed ${timeAgo(new Date(updatedAt).toISOString())}` : undefined}
      >
        <span className={`size-2 rounded-full ${live ? "animate-pulse bg-ok-600" : "bg-ink-300"}`} aria-hidden />
        {/* Paused, the picker itself says so; saying it twice read as a stutter. */}
        {live && <span className="font-semibold">Live</span>}
        <select
          aria-label="Refresh interval"
          value={String(refreshMs)}
          onChange={(e) => {
            const ms = Number(e.target.value);
            if (ms > 0) setLastLive(ms);
            onChange(ms);
          }}
          className="cursor-pointer border-0 bg-transparent py-0 pr-1 text-[13px] text-ink-500 focus:ring-0 focus:outline-none"
        >
          {REFRESH_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
      </label>
      <button
        type="button"
        onClick={() => onChange(live ? 0 : lastLive)}
        aria-label={live ? "Pause live updates" : "Resume live updates"}
        className="flex w-9 items-center justify-center border-l border-ink-100 text-ink-600 hover:bg-ink-50"
      >
        {live ? <Pause className="size-3.5" aria-hidden /> : <Play className="size-3.5" aria-hidden />}
      </button>
      <button
        type="button"
        onClick={onRefresh}
        aria-label="Refresh"
        title="Load the latest exchanges now"
        className="flex w-9 items-center justify-center border-l border-ink-100 text-ink-600 hover:bg-ink-50"
      >
        <RefreshCw className={`size-3.5 ${fetching ? "animate-spin" : ""}`} aria-hidden />
      </button>
    </span>
  );
}

