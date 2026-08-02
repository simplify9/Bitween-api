import { useMemo, useState, type ReactNode } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowUpRight,
  Cable,
  CalendarClock,
  ChevronDown,
  DownloadCloud,
  Plus,
  Search,
  Send,
  Workflow,
  Webhook,
  type LucideIcon,
} from "lucide-react";
import { api, type IntegrationRow } from "../../api";
import { useSession } from "../../auth/SessionContext";
import { useSessionCan } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { Table } from "../../components/ui/Table";
import { ConfirmDialog, Menu, MenuItem } from "../../components/ui/overlays";
import { HealthBadge, IntegrationStatusBadges } from "../../components/config/shared";
import { timeUntil } from "../../lib/dates";

type KindId = "api-gateways" | "bus-gateways" | "scheduled-jobs" | "internal" | "api-calls";

const KIND_META: Record<KindId, { label: string; icon: LucideIcon; legacy?: boolean }> = {
  "api-gateways": { label: "API gateways", icon: Webhook },
  "bus-gateways": { label: "Bus gateways", icon: Cable },
  "scheduled-jobs": { label: "Scheduled jobs", icon: CalendarClock },
  internal: { label: "Internal", icon: Workflow, legacy: true },
  "api-calls": { label: "API calls", icon: Send, legacy: true },
};

interface ExplorerRow {
  key: string;
  kind: KindId;
  name: string;
  details: string;
  /** What this is wired to — partners, routes, next run. Was the row drawer. */
  wiring: ReactNode;
  status: ReactNode;
  lastException: string | null;
  href: string;
  /** Receiving jobs only: check the source right now. */
  job: IntegrationRow | null;
}

/** Comma-separated links, truncated by the cell rather than by a count. */
function LinkList({ items }: { items: { id: number; name: string; href: string }[] }) {
  if (items.length === 0) return <span className="text-ink-400">—</span>;
  return (
    <span className="block truncate text-[13px]">
      {items.map((it, i) => (
        <span key={it.id}>
          {i > 0 && <span className="text-ink-300">, </span>}
          <Link to={it.href} className="text-ink-700 hover:text-crimson-700 hover:underline">
            {it.name}
          </Link>
        </span>
      ))}
    </span>
  );
}

function ReceiveNowButton({ r }: { r: IntegrationRow }) {
  const queryClient = useQueryClient();
  const [confirming, setConfirming] = useState(false);
  const receive = useMutation({
    mutationFn: () => api.receiveNow(r.id),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ["integration-rows"] }),
  });

  return (
    <>
      <Button
        size="sm"
        disabled={!r.enabled}
        title={r.enabled ? "Check the source right now." : "Enable the job first."}
        onClick={(e) => {
          e.stopPropagation();
          setConfirming(true);
        }}
      >
        <DownloadCloud className="size-3.5" /> Receive now
      </Button>
      {confirming && (
        <ConfirmDialog
          title="Receive now?"
          body={`${r.name} checks its source immediately — anything found becomes new exchanges, outside the regular schedule.`}
          confirmLabel="Receive now"
          onConfirm={async () => {
            await receive.mutateAsync();
          }}
          onClose={() => setConfirming(false)}
        />
      )}
    </>
  );
}

export function IntegrationsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const { session } = useSession();
  const q = searchParams.get("q") ?? "";
  const selected = useMemo(
    () => (searchParams.get("types")?.split(",").filter(Boolean) ?? []) as KindId[],
    [searchParams],
  );

  const permissions = session?.permissions ?? [];
  const canSeeApiGateways = permissions.includes("api-gateways.view");
  const canSeeBusGateways = permissions.includes("bus-gateways.view");

  const gateways = useQuery({
    queryKey: ["api-gateways"],
    queryFn: () => api.listApiGateways(),
    enabled: canSeeApiGateways,
  });
  const busGateways = useQuery({
    queryKey: ["bus-gateways"],
    queryFn: () => api.listBusGateways(),
    enabled: canSeeBusGateways,
  });
  const rows = useQuery({ queryKey: ["integration-rows"], queryFn: () => api.listIntegrationRows() });
  const canOperate = useSessionCan("subscriptions.operate");

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

  // single-select: choosing a kind replaces any other selection; "All" is the only way to clear it
  const selectKind = (kind: KindId) => setParam("types", selected.includes(kind) ? null : kind);

  // legacy filter buttons only appear when such integrations exist
  const hasInternal = (rows.data ?? []).some((r) => r.type === "Internal");
  const hasApiCalls = (rows.data ?? []).some((r) => r.type === "ApiCall");
  const visibleKinds = (Object.keys(KIND_META) as KindId[]).filter((k) =>
    k === "internal" ? hasInternal : k === "api-calls" ? hasApiCalls : true,
  );

  const allRows = useMemo<ExplorerRow[]>(() => {
    const out: ExplorerRow[] = [];
    for (const g of gateways.data ?? []) {
      out.push({
        key: `ag-${g.id}`,
        kind: "api-gateways",
        name: g.name,
        details: `/api/Gateway/${g.urlName}`,
        wiring: (
          <LinkList
            items={g.attachments.map((a) => ({
              id: a.partnerId,
              name: a.partnerName,
              href: `/partners/${a.partnerId}`,
            }))}
          />
        ),
        status:
          g.partnerCount > 0 ? (
            <Badge>{g.partnerCount} partner{g.partnerCount === 1 ? "" : "s"}</Badge>
          ) : (
            <Badge tone="warn">No partners</Badge>
          ),
        lastException: null,
        href: `/api-gateways/${g.id}`,
        job: null,
      });
    }
    for (const g of busGateways.data ?? []) {
      out.push({
        key: `bg-${g.id}`,
        kind: "bus-gateways",
        name: g.name,
        details: `Listens for ${g.informationTypeCode}`,
        wiring: (
          <LinkList
            items={g.routes.map((r) => ({
              id: r.id,
              name: r.integrationName,
              href: `/subscriptions/${r.integrationId}`,
            }))}
          />
        ),
        status:
          g.routeCount > 0 ? (
            <Badge>{g.routeCount} route{g.routeCount === 1 ? "" : "s"}</Badge>
          ) : (
            <Badge tone="warn">No routes</Badge>
          ),
        lastException: null,
        href: `/bus-gateways/${g.id}`,
        job: null,
      });
    }
    for (const r of rows.data ?? []) {
      const health = (
        <span className="inline-flex items-center gap-1">
          <IntegrationStatusBadges enabled={r.enabled} paused={r.paused} />
          <HealthBadge isRunning={r.isRunning} consecutiveFailures={r.consecutiveFailures} />
        </span>
      );
      if (r.type === "Receiving") {
        out.push({
          key: `in-${r.id}`,
          kind: "scheduled-jobs",
          name: r.name,
          details: [r.scheduleSummary, `pulls in ${r.informationTypeCode}`].filter(Boolean).join(" · "),
          wiring: (
            <span className="text-[13px] text-ink-600">
              next run {r.nextReceiveOn ? timeUntil(r.nextReceiveOn) : "—"}
            </span>
          ),
          status: health,
          lastException: r.lastException,
          href: `/subscriptions/${r.id}`,
          job: r,
        });
      } else if (r.type === "Internal" || r.type === "ApiCall") {
        out.push({
          key: `in-${r.id}`,
          kind: r.type === "Internal" ? "internal" : "api-calls",
          name: r.name,
          details: `Carries ${r.informationTypeCode}`,
          wiring: (
            <LinkList items={r.partners.map((p) => ({ id: p.id, name: p.name, href: `/partners/${p.id}` }))} />
          ),
          status: health,
          lastException: r.lastException,
          href: `/subscriptions/${r.id}`,
          job: null,
        });
      }
      // GatewayApiCall / BusGateway pipelines surface through their gateways;
      // Aggregation is not supported yet.
    }
    return out.sort((a, b) => a.name.localeCompare(b.name));
  }, [gateways.data, busGateways.data, rows.data]);

  const filtered = useMemo(() => {
    const needle = q.trim().toLowerCase();
    return allRows.filter(
      (r) =>
        (selected.length === 0 || selected.includes(r.kind)) &&
        (!needle || r.name.toLowerCase().includes(needle) || r.details.toLowerCase().includes(needle)),
    );
  }, [allRows, selected, q]);


  // create action follows the active filter
  const createables = [
    { kind: "api-gateways" as const, label: "New API gateway", permission: "api-gateways.create", run: () => navigate("/api-gateways/new") },
    { kind: "bus-gateways" as const, label: "New bus gateway", permission: "bus-gateways.create", run: () => navigate("/bus-gateways/new") },
    { kind: "scheduled-jobs" as const, label: "New scheduled job", permission: "subscriptions.create", run: () => navigate("/scheduled-jobs/new") },
  ].filter((c) => permissions.includes(c.permission));
  const activeCreatables =
    selected.length === 0 ? createables : createables.filter((c) => selected.includes(c.kind));
  const createAction =
    activeCreatables.length === 1 ? (
      <Button variant="primary" onClick={activeCreatables[0].run}>
        <Plus className="size-4" /> {activeCreatables[0].label}
      </Button>
    ) : activeCreatables.length > 1 ? (
      <Menu
        align="right"
        trigger={
          <Button variant="primary">
            <Plus className="size-4" /> New… <ChevronDown className="size-3.5" />
          </Button>
        }
      >
        {activeCreatables.map((c) => (
          <MenuItem key={c.kind} onSelect={c.run}>
            {c.label}
          </MenuItem>
        ))}
      </Menu>
    ) : undefined;

  const loading = rows.isPending || (canSeeApiGateways && gateways.isPending) || (canSeeBusGateways && busGateways.isPending);

  return (
    <div>
      <PageHeader
        title="Integrations"
        description="Every way documents flow — gateways partners call, bus listeners, and scheduled jobs."
        help={{
          title: "What's on this page?",
          body: (
            <>
              <p>
                The three entry points live here side by side: <strong>API gateways</strong>{" "}
                (partners push documents in), <strong>bus gateways</strong> (messages are picked
                off the bus) and <strong>scheduled jobs</strong> (documents are pulled in on a
                schedule). Expand a row for its details; open it for the full page.
              </p>
              <p>
                The pipelines behind a gateway are reached through the gateway itself. Legacy
                Internal/API-call integrations appear only when they exist.
              </p>
            </>
          ),
        }}
        actions={createAction}
      />

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <button
          onClick={() => setParam("types", null)}
          aria-pressed={selected.length === 0}
          className={`rounded-full px-3 py-1.5 text-[13px] font-medium transition-colors ${
            selected.length === 0 ? "bg-ink-900 text-white" : "border border-ink-200 bg-white text-ink-600 hover:border-ink-300 hover:bg-ink-50"
          }`}
        >
          All
        </button>
        {visibleKinds.map((kind) => {
          const meta = KIND_META[kind];
          const active = selected.includes(kind);
          return (
            <button
              key={kind}
              onClick={() => selectKind(kind)}
              aria-pressed={active}
              className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-[13px] font-medium transition-colors ${
                active ? "bg-ink-900 text-white" : "border border-ink-200 bg-white text-ink-600 hover:border-ink-300 hover:bg-ink-50"
              }`}
            >
              <meta.icon className="size-3.5" aria-hidden />
              {meta.label}
              {meta.legacy && <Badge tone="warn">Legacy</Badge>}
            </button>
          );
        })}
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

      {loading ? (
        <LoadingBlock label="Loading integrations…" />
      ) : filtered.length === 0 ? (
        <EmptyState icon={<Workflow />} title={q || selected.length ? "Nothing matches" : "Nothing here yet"}>
          {q || selected.length
            ? "Try a different search or filter."
            : "Create an API gateway, bus gateway or scheduled job to start moving documents."}
        </EmptyState>
      ) : (
        <Table
          rows={filtered}
          rowKey={(r) => r.key}
          minWidth="min-w-240"
          onRowClick={(r) => navigate(r.href)}
          columns={[
            {
              header: "Type",
              className: "whitespace-nowrap",
              cell: (r) => {
                const meta = KIND_META[r.kind];
                return (
                  <span className="inline-flex items-center gap-1.5 text-[13px] text-ink-600">
                    <meta.icon className="size-4 shrink-0 text-ink-400" aria-hidden />
                    {meta.label.replace(/s$/, "")}
                  </span>
                );
              },
            },
            {
              header: "Name",
              truncate: true,
              cell: (r) => <span className="block truncate font-medium text-ink-900">{r.name}</span>,
            },
            {
              header: "Details",
              truncate: true,
              cell: (r) => (
                <span className="block truncate text-ink-600" title={r.details}>
                  {r.details}
                </span>
              ),
            },
            { header: "Wired to", truncate: true, cell: (r) => r.wiring },
            { header: "Status", cell: (r) => r.status },
            {
              header: "Last error",
              truncate: true,
              cell: (r) =>
                r.lastException ? (
                  <span className="block truncate font-mono text-[11px] text-danger-700" title={r.lastException}>
                    {r.lastException}
                  </span>
                ) : (
                  <span className="text-ink-400">—</span>
                ),
            },
            {
              header: "",
              align: "right",
              className: "whitespace-nowrap",
              cell: (r) => (
                <span className="flex items-center justify-end gap-1">
                  {r.job && canOperate && <ReceiveNowButton r={r.job} />}
                  <button
                    onClick={(e) => {
                      e.stopPropagation();
                      navigate(r.href);
                    }}
                    aria-label={`Open ${r.name}`}
                    title="Open"
                    className="rounded-md p-1.5 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
                  >
                    <ArrowUpRight className="size-4" />
                  </button>
                </span>
              ),
            },
          ]}
        />
      )}
    </div>
  );
}
