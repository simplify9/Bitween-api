import { useMemo, useState } from "react";
import { Link, useNavigate } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Plus, Workflow } from "lucide-react";
import { api, type SubscriptionRow, type SubscriptionType } from "../../api";
import { Can } from "../../auth/guards";
import { useSessionCan } from "../../auth/useSessionCan";
import { PageHeader } from "../../components/layout/PageHeader";
import { Button, EmptyState, LoadError, LoadingBlock } from "../../components/ui/basics";
import { Pagination } from "../../components/ui/Pagination";
import { SearchSelect } from "../../components/ui/SearchSelect";
import { Select } from "../../components/ui/forms";
import { Table } from "../../components/ui/Table";
import { keys } from "../../api/queryKeys";
import { ExceptionLine } from "../../components/ui/Exception";
import { ManageCategoriesDialog } from "../../components/config/CategoryDialogs";
import { useCategories } from "../../components/config/categories";
import { NewSubscriptionDialog } from "./NewSubscriptionDialog";
import { HealthBadge, SubscriptionStatusBadges, LinkListCell, TypeBadge } from "../../components/config/shared";
import { SUBSCRIPTION_TYPE_LABELS, isLegacyType } from "../../components/config/subscriptionLabels";
import { useGatewayPartners, useSubscriptionsCache } from "../../components/config/lookups";
import { useListParams } from "../../lib/listParams";
import { NONE } from "../../lib/none";
import { subscriptionPath } from "../../lib/subscriptionPaths";
import { SearchBox } from "../../components/ui/SearchBox";

const STATUS_OPTIONS = [
  { value: "", label: "Any status" },
  { value: "false", label: "Active" },
  { value: "true", label: "Disabled" },
];

/** Filter order: what you'll have most of first, legacy last. */
const TYPE_ORDER: SubscriptionType[] = [
  "Receiving",
  "GatewayApiCall",
  "BusGateway",
  "Aggregation",
  "Response",
  "Internal",
  "ApiCall",
];

/**
 * Every subscription, of every type — the pipelines that move a document from
 * one place to another.
 *
 * Gateways are NOT rows here. A gateway is an entry point, not a pipeline, and
 * it has its own page; mixing them meant the table's columns had to mean
 * different things per row. `Receiving` jobs do appear on both this page and
 * Scheduled jobs, deliberately: here for the complete picture, there for the
 * schedule-specific columns.
 */
const PAGE_SIZE = 25;

export function SubscriptionsPage() {
  const { params: searchParams, set: setParam, searchText, setSearchText } = useListParams();
  const navigate = useNavigate();
  const q = searchParams.get("q") ?? "";
  const type = searchParams.get("type") as SubscriptionType | null;
  const informationTypeId = searchParams.get("informationTypeId")
    ? Number(searchParams.get("informationTypeId"))
    : null;
  const partnerId = searchParams.get("partnerId") ? Number(searchParams.get("partnerId")) : null;
  const categoryId = searchParams.get("categoryId") ? Number(searchParams.get("categoryId")) : null;
  const [managingCategories, setManagingCategories] = useState(false);
  const [choosingWay, setChoosingWay] = useState(false);
  const categories = useCategories().data ?? [];
  const allSubscriptions = useSubscriptionsCache().data ?? NONE;
  const categoryUsage = useMemo(() => {
    const counts = new Map<number, number>();
    for (const s of allSubscriptions) if (s.categoryId != null) counts.set(s.categoryId, (counts.get(s.categoryId) ?? 0) + 1);
    return counts;
  }, [allSubscriptions]);
  const inactiveParam = searchParams.get("inactive");
  const inactive = inactiveParam === "true" ? true : inactiveParam === "false" ? false : null;
  const offset = searchParams.get("offset") ? Number(searchParams.get("offset")) : 0;
  const canSeeInfoTypes = useSessionCan("documents.view");

  const rows = useQuery({
    queryKey: keys.subscriptions.rowsSearch({ q, type, informationTypeId, partnerId, categoryId, inactive, offset }),
    queryFn: () =>
      api.searchSubscriptionRows({
        search: q,
        type,
        informationTypeId,
        partnerId,
        categoryId,
        inactive,
        offset,
        limit: PAGE_SIZE,
      }),
    placeholderData: keepPreviousData,
  });
  const gatewayPartners = useGatewayPartners();
  const infoTypes = useQuery({ queryKey: keys.informationTypes.list, queryFn: () => api.listInformationTypes() }).data ?? [];
  const partners = useQuery({ queryKey: keys.partners.list, queryFn: () => api.listPartners() }).data ?? [];

  /** Its own partner (legacy types) plus any reached through a gateway. */
  const partnersFor = (r: SubscriptionRow) => {
    const own = r.partners;
    const viaGateway = (gatewayPartners.get(r.id) ?? []).filter((p) => !own.some((o) => o.id === p.id));
    return [...own, ...viaGateway];
  };


  const filtered = rows.data?.result ?? [];
  const total = rows.data?.total ?? 0;

  return (
    <div>
      <PageHeader
        title="All subscriptions"
        description="Every pipeline that moves a document — what comes in, how it's transformed, where it goes."
        actions={
          <div className="flex gap-2">
            <Button onClick={() => setManagingCategories(true)}>Categories</Button>
            <Can permission="subscriptions.create">
              <Button variant="primary" onClick={() => setChoosingWay(true)}>
                <Plus className="size-4" /> New subscription
              </Button>
            </Can>
          </div>
        }
        help={{
          title: "What's on this page?",
          body: (
            <>
              <p>
                A subscription is one <strong>pipeline</strong>: it receives a document, optionally
                maps it, and hands it on. Every type is listed here, including legacy ones.
              </p>
              <p>
                The <strong>entry points</strong> live on their own pages — API gateways (partners
                push in), bus gateways (messages off the bus) and scheduled jobs (pulled in on a
                schedule). A scheduled job is also a subscription, so it appears in both places.
              </p>
              <p>
                <strong>New subscription</strong> asks what starts it and takes you there: a
                gateway subscription is created while attaching a partner or adding a route, so it
                is wired up the moment it exists; the other kinds have their own pages.
              </p>
            </>
          ),
        }}
      />

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <button
          onClick={() => setParam("type", null)}
          aria-pressed={!type}
          className={`rounded-full px-3 py-1.5 text-[13px] font-medium transition-colors ${
            !type
              ? "bg-ink-900 text-white"
              : "border border-ink-200 bg-white text-ink-600 hover:border-ink-300 hover:bg-ink-50"
          }`}
        >
          All
        </button>
        {/* The legacy types only where they are still in use: a chip for a kind of subscription
            nobody can create, on an instance that has none, is a filter that can only say "none". */}
        {TYPE_ORDER.filter((t) => !isLegacyType(t) || type === t || allSubscriptions.some((s) => s.type === t)).map((t) => (
          <button
            key={t}
            onClick={() => setParam("type", type === t ? null : t)}
            aria-pressed={type === t}
            className={`rounded-full px-3 py-1.5 text-[13px] font-medium transition-colors ${
              type === t
                ? "bg-ink-900 text-white"
                : "border border-ink-200 bg-white text-ink-600 hover:border-ink-300 hover:bg-ink-50"
            }`}
          >
            {SUBSCRIPTION_TYPE_LABELS[t]}
          </button>
        ))}
        <SearchBox value={searchText} onChange={setSearchText} label="Search subscriptions" placeholder="Search" className="ml-auto w-full max-w-55 sm:w-auto" />
      </div>

      <div className="mb-4 grid grid-cols-2 gap-2 sm:grid-cols-4">
        <SearchSelect
          aria-label="Filter by information type"
          size="sm"
          clearLabel="Any information type"
          value={informationTypeId?.toString() ?? ""}
          onChange={(v) => setParam("informationTypeId", v || null)}
          options={infoTypes.map((t) => ({ value: String(t.id), label: t.name, code: t.code }))}
        />
        <SearchSelect
          aria-label="Filter by partner"
          size="sm"
          clearLabel="Any partner"
          value={partnerId?.toString() ?? ""}
          onChange={(v) => setParam("partnerId", v || null)}
          options={partners.map((p) => ({ value: String(p.id), label: p.name }))}
        />
        <SearchSelect
          aria-label="Filter by category"
          size="sm"
          clearLabel="Any category"
          value={categoryId?.toString() ?? ""}
          onChange={(v) => setParam("categoryId", v || null)}
          options={categories.map((c) => ({ value: String(c.id), label: c.code }))}
        />
        <Select
          aria-label="Filter by status"
          className="!h-8 text-[13px]"
          value={inactiveParam ?? ""}
          onChange={(e) => setParam("inactive", e.target.value || null)}
          options={STATUS_OPTIONS}
        />
      </div>

      {choosingWay && <NewSubscriptionDialog onClose={() => setChoosingWay(false)} />}
      {managingCategories && (
        <ManageCategoriesDialog usage={categoryUsage} onClose={() => setManagingCategories(false)} />
      )}

      {rows.isPending ? (
        <LoadingBlock label="Loading subscriptions…" />
      ) : rows.isError ? (
        <LoadError error={rows.error} what="subscriptions" onRetry={() => void rows.refetch()} />
      ) : filtered.length === 0 ? (
        <EmptyState
          icon={<Workflow />}
          title={
            q || type || informationTypeId || partnerId || categoryId || inactive !== null ? "Nothing matches" : "No subscriptions yet"
          }
          action={
            q || type || informationTypeId || partnerId || categoryId || inactive !== null ? undefined : (
              <Can permission="subscriptions.create">
                <Button variant="primary" onClick={() => setChoosingWay(true)}>
                  <Plus className="size-4" /> New subscription
                </Button>
              </Can>
            )
          }
        >
          {q || type || informationTypeId || partnerId || categoryId || inactive !== null
            ? "Try a different search or filter."
            : "Create a subscription to start moving documents."}
        </EmptyState>
      ) : (
        <Table
          rows={filtered}
          rowKey={(r) => r.id}
          minWidth="min-w-220"
          onRowClick={(r) => navigate(subscriptionPath(r.id, r.type))}
          footer={
            <Pagination
              offset={offset}
              limit={PAGE_SIZE}
              total={total}
              onOffsetChange={(o) => setParam("offset", String(o))}
            />
          }
          columns={[
            {
              header: "Subscription",
              wrap: true,
              cell: (r) => <span className="block font-medium text-ink-900">{r.name}</span>,
            },
            { header: "Type", cell: (r) => <TypeBadge type={r.type} /> },
            {
              header: "Information type",
              cell: (r) =>
                canSeeInfoTypes ? (
                  <Link
                    to={`/information-types/${r.informationTypeId}`}
                    onClick={(e) => e.stopPropagation()}
                    className="font-mono text-xs text-ink-600 hover:text-crimson-700 hover:underline"
                  >
                    {r.informationTypeCode}
                  </Link>
                ) : (
                  <code className="font-mono text-xs text-ink-600">{r.informationTypeCode}</code>
                ),
            },
            {
              header: "Partner",
              wrap: true,
              cell: (r) => (
                <LinkListCell
                  label="partners"
                  items={partnersFor(r).map((p) => ({
                    key: p.id,
                    name: p.name,
                    href: `/partners/${p.id}`,
                  }))}
                />
              ),
            },
            {
              header: "Status",
              cell: (r) => (
                <span className="inline-flex items-center gap-1">
                  <SubscriptionStatusBadges enabled={r.enabled} paused={r.paused} />
                  <HealthBadge isRunning={r.isRunning} consecutiveFailures={r.consecutiveFailures} />
                </span>
              ),
            },
            {
              header: "Last error",
              truncate: true,
              cell: (r) =>
                r.lastException ? (
                  <ExceptionLine text={r.lastException} className="text-[12px] text-danger-700" />
                ) : (
                  <span className="text-ink-500">—</span>
                ),
            },
          ]}
        />
      )}
    </div>
  );
}
