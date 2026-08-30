import { useMemo, useState } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { FileText, Plus, Search } from "lucide-react";
import { api } from "../../api";
import { Can } from "../../auth/guards";
import { InformationTypeDialog } from "../../components/config/InformationTypeDialog";
import { SubscriptionMultiFilter } from "../../components/config/SubscriptionMultiFilter";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { Select } from "../../components/ui/forms";
import { CodeBadge } from "../../components/ui/Panel";
import { Pagination } from "../../components/ui/Pagination";
import { Table } from "../../components/ui/Table";
import { UsedByCell, useSubscriptionsCache } from "../../components/config/shared";
import { keys } from "../../api/queryKeys";

const PAGE_SIZE = 25;

const FORMAT_OPTIONS = [
  { value: "", label: "Any format" },
  { value: "Json", label: "JSON" },
  { value: "Xml", label: "XML" },
];

const BUS_OPTIONS = [
  { value: "", label: "Any bus status" },
  { value: "true", label: "On the bus" },
  { value: "false", label: "Not on the bus" },
];

/** ?subscriptions=3,5 — no id can be 0, so filter/join round-trip cleanly through this. */
const parseIds = (raw: string | null): number[] =>
  raw
    ? raw
        .split(",")
        .map(Number)
        .filter((n) => Number.isInteger(n) && n > 0)
    : [];

export function InformationTypesPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const [creating, setCreating] = useState(false);
  const q = searchParams.get("q") ?? "";
  const format = searchParams.get("format") as "Json" | "Xml" | null;
  const busParam = searchParams.get("bus");
  const busEnabled = busParam === "true" ? true : busParam === "false" ? false : null;
  const subscriptionIds = parseIds(searchParams.get("subscriptions"));
  const offset = searchParams.get("offset") ? Number(searchParams.get("offset")) : 0;

  const subscriptions = useSubscriptionsCache().data ?? [];

  // The backend's Search endpoint has no "id is in this set" filter, so filtering by
  // which subscriptions use a type can't be pushed down like name/format/bus can. Falls
  // back to the full list, filtered and paged client-side, only while that filter is
  // active — same trade-off as the Partners and Global values pages.
  const filtering = subscriptionIds.length > 0;

  const serverSearch = useQuery({
    queryKey: keys.informationTypes.search({ q, format, busEnabled, offset }),
    queryFn: () => api.searchInformationTypes({ search: q, format, busEnabled, offset, limit: PAGE_SIZE }),
    placeholderData: keepPreviousData,
    enabled: !filtering,
  });
  const allTypes = useQuery({
    queryKey: keys.informationTypes.list,
    queryFn: () => api.listInformationTypes(),
    enabled: filtering,
  });

  const setParam = (key: string, value: string | null, resetOffset = true) =>
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        if (value) next.set(key, value);
        else next.delete(key);
        if (resetOffset) next.delete("offset");
        return next;
      },
      { replace: key === "q" },
    );

  const filteredSorted = useMemo(() => {
    if (!filtering) return [];
    const needle = q.trim().toLowerCase();
    const wanted = new Set(subscriptionIds);
    return (allTypes.data ?? []).filter((t) => {
      if (needle && !t.name.toLowerCase().includes(needle)) return false;
      if (format && t.format !== format) return false;
      if (busEnabled !== null && t.busEnabled !== busEnabled) return false;
      const usedBy = subscriptions.filter((s) => s.informationTypeId === t.id);
      return usedBy.some((s) => wanted.has(s.id));
    });
  }, [filtering, allTypes.data, q, format, busEnabled, subscriptionIds, subscriptions]);

  const isPending = filtering ? allTypes.isPending : serverSearch.isPending;
  const rows = filtering ? filteredSorted.slice(offset, offset + PAGE_SIZE) : (serverSearch.data?.result ?? []);
  const total = filtering ? filteredSorted.length : (serverSearch.data?.total ?? 0);

  return (
    <div>
      <PageHeader
        title="Information types"
        description="The kinds of business documents flowing between you and your partners — each with a short code used across the system."
        help={{
          title: "How information types work",
          body: (
            <>
              <p>
                Every exchange carries exactly one information type (a purchase order, a shipment
                update…). The <strong>code</strong> is its short identity everywhere in Bitween;
                the name is the friendly label.
              </p>
              <p>
                <strong>Promoted properties</strong> pull values out of the payload (by JSON path
                or XML path) so routes and filters can match on them — for example routing orders
                by store number.
              </p>
            </>
          ),
        }}
        actions={
          <Can permission="documents.create">
            <Button variant="primary" onClick={() => setCreating(true)}>
              <Plus className="size-4" /> New information type
            </Button>
          </Can>
        }
      />

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <div className="relative w-full max-w-xs">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-400" />
          <input
            type="search"
            value={q}
            onChange={(e) => setParam("q", e.target.value || null)}
            placeholder="Search by name"
            aria-label="Search information types"
            className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
          />
        </div>
        <div className="w-36">
          <Select
            aria-label="Filter by format"
            className="!h-8 text-[13px]"
            value={format ?? ""}
            onChange={(e) => setParam("format", e.target.value || null)}
            options={FORMAT_OPTIONS}
          />
        </div>
        <div className="w-44">
          <Select
            aria-label="Filter by bus status"
            className="!h-8 text-[13px]"
            value={busParam ?? ""}
            onChange={(e) => setParam("bus", e.target.value || null)}
            options={BUS_OPTIONS}
          />
        </div>
        <div className="w-60">
          <SubscriptionMultiFilter
            subscriptions={subscriptions}
            selected={subscriptionIds}
            onChange={(ids) => setParam("subscriptions", ids.length ? ids.join(",") : null)}
            label="Filter by subscription"
          />
        </div>
      </div>

      {isPending ? (
        <LoadingBlock label="Loading information types…" />
      ) : rows.length === 0 ? (
        <EmptyState
          icon={<FileText />}
          title={q || format || busParam || subscriptionIds.length > 0 ? "No information types match" : "No information types yet"}
        >
          {q || format || busParam || subscriptionIds.length > 0
            ? "Try a different search or filter."
            : "Define the first kind of document your subscriptions will carry."}
        </EmptyState>
      ) : (
        <Table
          rows={rows}
          rowKey={(t) => t.id}
          minWidth="min-w-220"
          onRowClick={(t) => navigate(`/information-types/${t.id}`)}
          footer={
            <Pagination
              offset={offset}
              limit={PAGE_SIZE}
              total={total}
              onOffsetChange={(o) => setParam("offset", String(o), false)}
            />
          }
          columns={[
            { header: "Code", cell: (t) => <CodeBadge code={t.code} name={t.name} /> },
            { header: "Name", cell: (t) => <span className="font-medium text-ink-900">{t.name}</span> },
            { header: "Format", cell: (t) => <Badge>{t.format.toUpperCase()}</Badge> },
            {
              header: "Bus",
              cell: (t) =>
                t.busEnabled ? (
                  <code className="font-mono text-xs text-ink-600">{t.busMessageTypeName}</code>
                ) : (
                  <span className="text-ink-400">—</span>
                ),
            },
            {
              // The promoted keys themselves — these are what exchange search
              // filters on, so knowing there are "3" of them helps nobody.
              header: "Promoted",
              truncate: true,
              cell: (t) =>
                t.promotedProperties.length > 0 ? (
                  <span
                    className="block truncate font-mono text-xs text-ink-600"
                    title={t.promotedProperties.map((p) => p.key).join(", ")}
                  >
                    {t.promotedProperties.map((p) => p.key).join(", ")}
                  </span>
                ) : (
                  <span className="text-ink-400">—</span>
                ),
            },
            {
              header: "Duplicates",
              cell: (t) => (
                <span className="text-[13px] text-ink-600">
                  {t.duplicateIntervalMinutes > 0 ? `${t.duplicateIntervalMinutes}m window` : "Off"}
                </span>
              ),
            },
            {
              header: "Used by",
              truncate: true,
              cell: (t) => <UsedByCell items={subscriptions.filter((s) => s.informationTypeId === t.id)} />,
            },
          ]}
        />
      )}


      {creating && (
        <InformationTypeDialog
          typeId={null}
          onClose={() => setCreating(false)}
          onSaved={({ id }) => navigate(`/information-types/${id}`)}
        />
      )}
    </div>
  );
}
