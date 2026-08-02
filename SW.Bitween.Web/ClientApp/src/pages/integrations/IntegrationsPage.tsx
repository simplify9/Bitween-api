import { useMemo } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { Plus, Search, Workflow } from "lucide-react";
import { api, type IntegrationRow, type IntegrationType } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { Table } from "../../components/ui/Table";
import {
  HealthBadge,
  INTEGRATION_TYPE_LABELS,
  IntegrationStatusBadges,
  LinkListCell,
  TypeBadge,
  useGatewayPartners,
} from "../../components/config/shared";

/** Filter order: what you'll have most of first, legacy last. */
const TYPE_ORDER: IntegrationType[] = [
  "Receiving",
  "GatewayApiCall",
  "BusGateway",
  "Aggregation",
  "Internal",
  "ApiCall",
];

/**
 * Every integration, of every type — the pipelines that move a document from
 * one place to another.
 *
 * Gateways are NOT rows here. A gateway is an entry point, not a pipeline, and
 * it has its own page; mixing them meant the table's columns had to mean
 * different things per row. `Receiving` jobs do appear on both this page and
 * Scheduled jobs, deliberately: here for the complete picture, there for the
 * schedule-specific columns.
 */
export function IntegrationsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const q = searchParams.get("q") ?? "";
  const type = searchParams.get("type") as IntegrationType | null;
  const canSeeInfoTypes = useSessionCan("documents.view");

  const rows = useQuery({ queryKey: ["integration-rows"], queryFn: () => api.listIntegrationRows() });
  const gatewayPartners = useGatewayPartners();

  /** Its own partner (legacy types) plus any reached through a gateway. */
  const partnersFor = (r: IntegrationRow) => {
    const own = r.partners;
    const viaGateway = (gatewayPartners.get(r.id) ?? []).filter((p) => !own.some((o) => o.id === p.id));
    return [...own, ...viaGateway];
  };

  const setParam = (key: string, value: string | null) =>
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        if (value) next.set(key, value);
        else next.delete(key);
        return next;
      },
      { replace: key === "q" },
    );

  // Only offer a type you actually have — an empty filter teaches nothing.
  const presentTypes = useMemo(() => {
    const present = new Set((rows.data ?? []).map((r) => r.type));
    return TYPE_ORDER.filter((t) => present.has(t));
  }, [rows.data]);

  const filtered = useMemo(() => {
    const needle = q.trim().toLowerCase();
    return (rows.data ?? [])
      .filter((r) => !type || r.type === type)
      .filter(
        (r) =>
          !needle ||
          r.name.toLowerCase().includes(needle) ||
          r.informationTypeCode.toLowerCase().includes(needle),
      )
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [rows.data, type, q]);

  return (
    <div>
      <PageHeader
        title="Integrations"
        description="Every pipeline that moves a document — what comes in, how it's transformed, where it goes."
        help={{
          title: "What's on this page?",
          body: (
            <>
              <p>
                An integration is one <strong>pipeline</strong>: it receives a document, optionally
                maps it, and hands it on. Every type is listed here, including legacy ones.
              </p>
              <p>
                The <strong>entry points</strong> live on their own pages — API gateways (partners
                push in), bus gateways (messages off the bus) and scheduled jobs (pulled in on a
                schedule). A scheduled job is also an integration, so it appears in both places.
              </p>
            </>
          ),
        }}
        actions={
          <Can permission="subscriptions.create">
            <Button variant="primary" onClick={() => navigate("/subscriptions/new")}>
              <Plus className="size-4" /> New integration
            </Button>
          </Can>
        }
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
        {presentTypes.map((t) => (
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
            {INTEGRATION_TYPE_LABELS[t]}
          </button>
        ))}
        <div className="relative ml-auto w-full max-w-55 sm:w-auto">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-400" />
          <input
            type="search"
            value={q}
            onChange={(e) => setParam("q", e.target.value || null)}
            placeholder="Search"
            aria-label="Search integrations"
            className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
          />
        </div>
      </div>

      {rows.isPending ? (
        <LoadingBlock label="Loading integrations…" />
      ) : filtered.length === 0 ? (
        <EmptyState icon={<Workflow />} title={q || type ? "Nothing matches" : "No integrations yet"}>
          {q || type
            ? "Try a different search or filter."
            : "Create an integration to start moving documents."}
        </EmptyState>
      ) : (
        <Table
          rows={filtered}
          rowKey={(r) => r.id}
          minWidth="min-w-220"
          onRowClick={(r) => navigate(`/subscriptions/${r.id}`)}
          columns={[
            {
              header: "Integration",
              truncate: true,
              cell: (r) => <span className="block truncate font-medium text-ink-900">{r.name}</span>,
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
              truncate: true,
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
                  <IntegrationStatusBadges enabled={r.enabled} paused={r.paused} />
                  <HealthBadge isRunning={r.isRunning} consecutiveFailures={r.consecutiveFailures} />
                </span>
              ),
            },
            {
              header: "Last error",
              truncate: true,
              cell: (r) =>
                r.lastException ? (
                  <span
                    className="block truncate font-mono text-[11px] text-danger-700"
                    title={r.lastException}
                  >
                    {r.lastException}
                  </span>
                ) : (
                  <span className="text-ink-400">—</span>
                ),
            },
          ]}
        />
      )}
    </div>
  );
}
