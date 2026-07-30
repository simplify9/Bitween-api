import { Fragment, useMemo, useState, type ReactNode } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowUpRight,
  Cable,
  CalendarClock,
  ChevronDown,
  ChevronRight,
  DownloadCloud,
  Plus,
  Search,
  Send,
  Workflow,
  Webhook,
  type LucideIcon,
} from "lucide-react";
import { api, type ApiGatewayRow, type BusGatewayRow, type IntegrationRow } from "../../api";
import { useSession } from "../../auth/SessionContext";
import { useSessionCan } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { ConfirmDialog, Menu, MenuItem } from "../../components/ui/overlays";
import { HealthBadge, IntegrationStatusBadges } from "../../components/config/shared";
import { matchSummary } from "../../lib/match";
import { formatDate, timeUntil } from "../../lib/dates";

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
  status: ReactNode;
  createdOn: string;
  href: string;
  drawer: ReactNode;
}

function DrawerLine({ children }: { children: ReactNode }) {
  return <div className="flex flex-wrap items-center gap-2 text-[13px] text-ink-600">{children}</div>;
}

function ApiGatewayDrawer({ g }: { g: ApiGatewayRow }) {
  return (
    <div className="space-y-1.5">
      <DrawerLine>
        <code className="font-mono text-xs text-ink-500">/api/Gateway/{g.urlName}/sync</code>
        <span className="text-ink-300">·</span>
        <code className="font-mono text-xs text-ink-500">/async</code>
      </DrawerLine>
      {g.attachments.length === 0 ? (
        <DrawerLine>No partners attached — the gateway answers 401 to everyone.</DrawerLine>
      ) : (
        g.attachments.map((a) => (
          <DrawerLine key={a.partnerId}>
            <Link to={`/partners/${a.partnerId}`} className="font-medium text-ink-800 hover:text-crimson-700 hover:underline">
              {a.partnerName}
            </Link>
            runs
            <Link
              to={`/subscriptions/${a.integrationId}`}
              className="font-medium text-ink-800 hover:text-crimson-700 hover:underline"
            >
              {a.integrationName}
            </Link>
          </DrawerLine>
        ))
      )}
    </div>
  );
}

function BusGatewayDrawer({ g }: { g: BusGatewayRow }) {
  return (
    <div className="space-y-1.5">
      {g.routes.length === 0 ? (
        <DrawerLine>No routes — every {g.informationTypeCode} message is ignored by this gateway.</DrawerLine>
      ) : (
        g.routes.map((r) => (
          <DrawerLine key={r.id}>
            <code className="font-mono text-xs text-ink-500">{matchSummary(r.matchExpression)}</code>
            <span className="text-ink-300">→</span>
            <Link
              to={`/subscriptions/${r.integrationId}`}
              className="font-medium text-ink-800 hover:text-crimson-700 hover:underline"
            >
              {r.integrationName}
            </Link>
            {r.partnerName && (
              <span className="text-ink-500">
                for{" "}
                <Link to={`/partners/${r.partnerId}`} className="font-medium text-ink-800 hover:text-crimson-700 hover:underline">
                  {r.partnerName}
                </Link>
              </span>
            )}
          </DrawerLine>
        ))
      )}
    </div>
  );
}

function JobDrawer({ r }: { r: IntegrationRow }) {
  const queryClient = useQueryClient();
  const canOperate = useSessionCan("subscriptions.operate");
  const [confirming, setConfirming] = useState(false);
  const receive = useMutation({
    mutationFn: () => api.receiveNow(r.id),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ["integration-rows"] }),
  });

  return (
    <div className="space-y-1.5">
      <DrawerLine>
        {r.scheduleSummary}
        <span className="text-ink-300">·</span>
        next run {r.nextReceiveOn ? timeUntil(r.nextReceiveOn) : "—"}
      </DrawerLine>
      {r.lastException && (
        <pre className="max-h-24 overflow-auto rounded-md bg-danger-50 px-2.5 py-1.5 font-mono text-[11px] leading-relaxed whitespace-pre-wrap text-danger-800">
          {r.lastException}
        </pre>
      )}
      {canOperate && (
        <Button
          size="sm"
          disabled={!r.enabled}
          title={r.enabled ? "Check the source right now." : "Enable the job first."}
          onClick={() => setConfirming(true)}
        >
          <DownloadCloud className="size-3.5" /> Receive now
        </Button>
      )}
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
    </div>
  );
}

function LegacyDrawer({ r }: { r: IntegrationRow }) {
  return (
    <div className="space-y-1.5">
      <DrawerLine>
        Carries{" "}
        <Link to={`/information-types/${r.informationTypeId}`} className="hover:underline">
          <code className="font-mono text-xs text-ink-500">{r.informationTypeCode}</code>
        </Link>
        {r.partners.length > 0 && (
          <>
            <span className="text-ink-300">·</span> for{" "}
            {r.partners.map((p, i) => (
              <span key={p.id}>
                {i > 0 && ", "}
                <Link to={`/partners/${p.id}`} className="text-ink-800 hover:text-crimson-700 hover:underline">
                  {p.name}
                </Link>
              </span>
            ))}
          </>
        )}
      </DrawerLine>
      {r.lastException && (
        <pre className="max-h-24 overflow-auto rounded-md bg-danger-50 px-2.5 py-1.5 font-mono text-[11px] leading-relaxed whitespace-pre-wrap text-danger-800">
          {r.lastException}
        </pre>
      )}
      <DrawerLine>Legacy type — editable, but new ones can't be created.</DrawerLine>
    </div>
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
  const [open, setOpen] = useState<Set<string>>(new Set());

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
        status:
          g.partnerCount > 0 ? (
            <Badge>{g.partnerCount} partner{g.partnerCount === 1 ? "" : "s"}</Badge>
          ) : (
            <Badge tone="warn">No partners</Badge>
          ),
        createdOn: g.createdOn,
        href: `/api-gateways/${g.id}`,
        drawer: <ApiGatewayDrawer g={g} />,
      });
    }
    for (const g of busGateways.data ?? []) {
      out.push({
        key: `bg-${g.id}`,
        kind: "bus-gateways",
        name: g.name,
        details: `Listens for ${g.informationTypeCode}`,
        status:
          g.routeCount > 0 ? (
            <Badge>{g.routeCount} route{g.routeCount === 1 ? "" : "s"}</Badge>
          ) : (
            <Badge tone="warn">No routes</Badge>
          ),
        createdOn: g.createdOn,
        href: `/bus-gateways/${g.id}`,
        drawer: <BusGatewayDrawer g={g} />,
      });
    }
    for (const r of rows.data ?? []) {
      if (r.type === "Receiving") {
        out.push({
          key: `in-${r.id}`,
          kind: "scheduled-jobs",
          name: r.name,
          details: [r.scheduleSummary, `pulls in ${r.informationTypeCode}`].filter(Boolean).join(" · "),
          status: (
            <span className="inline-flex items-center gap-1">
              <IntegrationStatusBadges enabled={r.enabled} paused={r.paused} />
              <HealthBadge isRunning={r.isRunning} consecutiveFailures={r.consecutiveFailures} />
            </span>
          ),
          createdOn: r.createdOn,
          href: `/subscriptions/${r.id}`,
          drawer: <JobDrawer r={r} />,
        });
      } else if (r.type === "Internal" || r.type === "ApiCall") {
        out.push({
          key: `in-${r.id}`,
          kind: r.type === "Internal" ? "internal" : "api-calls",
          name: r.name,
          details: `Carries ${r.informationTypeCode}${r.partners.length ? ` · ${r.partners.map((p) => p.name).join(", ")}` : ""}`,
          status: (
            <span className="inline-flex items-center gap-1">
              <IntegrationStatusBadges enabled={r.enabled} paused={r.paused} />
              <HealthBadge isRunning={r.isRunning} consecutiveFailures={r.consecutiveFailures} />
            </span>
          ),
          createdOn: r.createdOn,
          href: `/subscriptions/${r.id}`,
          drawer: <LegacyDrawer r={r} />,
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

  const toggleOpen = (key: string) =>
    setOpen((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });

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
        <div className="overflow-x-auto rounded-xl border border-ink-200 bg-white">
          <table className="w-full min-w-180 text-sm">
            <thead>
              <tr className="border-b border-ink-100 text-left text-xs text-ink-500">
                <th className="w-10 px-3 py-2.5" />
                <th className="px-3 py-2.5 font-medium">Type</th>
                <th className="px-3 py-2.5 font-medium">Name</th>
                <th className="px-3 py-2.5 font-medium">Details</th>
                <th className="px-3 py-2.5 font-medium">Status</th>
                <th className="px-3 py-2.5 font-medium">Created</th>
                <th className="w-10 px-3 py-2.5" />
              </tr>
            </thead>
            <tbody>
              {filtered.map((r) => {
                const meta = KIND_META[r.kind];
                const expanded = open.has(r.key);
                return (
                  <Fragment key={r.key}>
                    <tr
                      onClick={() => toggleOpen(r.key)}
                      className="cursor-pointer border-b border-ink-100 last:border-b-0 hover:bg-ink-50"
                    >
                      <td className="px-3 py-3">
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            toggleOpen(r.key);
                          }}
                          aria-expanded={expanded}
                          aria-label={`Details for ${r.name}`}
                          className="rounded-md p-1 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
                        >
                          {expanded ? <ChevronDown className="size-3.5" /> : <ChevronRight className="size-3.5" />}
                        </button>
                      </td>
                      <td className="px-3 py-3">
                        <span className="inline-flex items-center gap-2">
                          <span className="flex size-7 shrink-0 items-center justify-center rounded-lg bg-ink-100 text-ink-700" aria-hidden>
                            <meta.icon className="size-4" />
                          </span>
                          <span className="text-[13px] text-ink-600">
                            {meta.label.replace(/s$/, "")}
                          </span>
                        </span>
                      </td>
                      <td className="px-3 py-3 font-medium text-ink-900">{r.name}</td>
                      <td className="max-w-90 truncate px-3 py-3 text-ink-600" title={r.details}>
                        {r.details}
                      </td>
                      <td className="px-3 py-3">{r.status}</td>
                      <td className="px-3 py-3 whitespace-nowrap text-ink-500">{formatDate(r.createdOn)}</td>
                      <td className="px-3 py-3">
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
                      </td>
                    </tr>
                    {expanded && (
                      <tr className="border-b border-ink-100 last:border-b-0">
                        <td />
                        <td colSpan={6} className="px-3 pt-0.5 pb-3">
                          <div className="rounded-lg bg-ink-50 px-3.5 py-3">{r.drawer}</div>
                        </td>
                      </tr>
                    )}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
