import { Fragment, useState } from "react";
import { Link } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeftRight, ChevronDown, ChevronRight, Workflow } from "lucide-react";
import {
  api,
  type ExchangeDocStage,
  type ExchangeDocument,
  type ExchangeRef,
  type IntegrationInfo,
  type IntegrationSetupRef,
  type IntegrationType,
} from "../../api";
import { Badge } from "../ui/basics";
import { timeAgo } from "../../lib/dates";

/** Display names for integration types; Internal and ApiCall are legacy. */
export const INTEGRATION_TYPE_LABELS: Record<IntegrationType, string> = {
  Receiving: "Receiver",
  GatewayApiCall: "API gateway",
  BusGateway: "Bus gateway",
  Internal: "Internal",
  ApiCall: "API call",
  Aggregation: "Aggregation",
};

export const isLegacyType = (type: IntegrationType) => type === "Internal" || type === "ApiCall";

export function TypeBadge({ type }: { type: IntegrationType }) {
  return (
    <span className="inline-flex items-center gap-1">
      <Badge>{INTEGRATION_TYPE_LABELS[type]}</Badge>
      {isLegacyType(type) && <Badge tone="warn">Legacy</Badge>}
    </span>
  );
}

/** Enabled/paused pair — an integration can be both enabled and paused. */
export function IntegrationStatusBadges({ enabled, paused }: { enabled: boolean; paused: boolean }) {
  return (
    <span className="inline-flex items-center gap-1">
      {enabled ? <Badge tone="ok">Active</Badge> : <Badge>Disabled</Badge>}
      {paused && <Badge tone="warn">Paused</Badge>}
    </span>
  );
}

export function HealthBadge({
  isRunning,
  consecutiveFailures,
}: {
  isRunning: boolean;
  consecutiveFailures: number;
}) {
  if (consecutiveFailures > 0)
    return (
      <Badge tone="crimson">
        {consecutiveFailures} failure{consecutiveFailures === 1 ? "" : "s"}
      </Badge>
    );
  if (isRunning) return <Badge tone="ok">Running</Badge>;
  return <Badge>Idle</Badge>;
}

export function ExchangeStatusBadge({ status }: { status: ExchangeRef["status"] }) {
  if (status === "success") return <Badge tone="ok">Success</Badge>;
  if (status === "failed") return <Badge tone="crimson">Failed</Badge>;
  if (status === "badResponse") return <Badge tone="warn">Bad response</Badge>;
  return <Badge tone="neutral">Processing</Badge>;
}

const DOC_STAGES: ExchangeDocStage[] = ["Input", "Mapped", "Handled"];

/**
 * The document trail of one exchange: a tab per pipeline stage. Stages
 * that produced nothing (e.g. after a failure) stay visible but disabled,
 * so the pipeline shape — and where it stopped — is always readable.
 */
function ExchangeDocsDrawer({ documents }: { documents: ExchangeDocument[] }) {
  const [stage, setStage] = useState<ExchangeDocStage>(documents[0]?.stage ?? "Input");
  const active = documents.find((d) => d.stage === stage);

  return (
    <div className="ml-6">
      <div className="mb-1.5 flex items-center gap-1">
        {DOC_STAGES.map((s) => {
          const exists = documents.some((d) => d.stage === s);
          return (
            <button
              key={s}
              disabled={!exists}
              aria-pressed={s === stage}
              title={exists ? undefined : "This stage produced no document"}
              onClick={() => setStage(s)}
              className={`rounded-md px-2 py-0.5 text-xs font-medium ${
                s === stage
                  ? "bg-ink-800 text-ink-50"
                  : exists
                    ? "text-ink-600 hover:bg-ink-100"
                    : "cursor-not-allowed text-ink-300"
              }`}
            >
              {s}
            </button>
          );
        })}
      </div>
      <pre className="max-h-48 overflow-auto rounded-lg bg-ink-950 px-3 py-2.5 font-mono text-[11px] leading-relaxed text-ink-100">
        {active?.content}
      </pre>
    </div>
  );
}

/**
 * Compact recent-traffic list for hub pages. With `expandable`, each row
 * opens a drawer showing the documents from each pipeline stage.
 */
export function ExchangesList({
  items,
  expandable = false,
}: {
  items: ExchangeRef[];
  expandable?: boolean;
}) {
  const [open, setOpen] = useState<Set<string>>(new Set());

  const toggle = (id: string) =>
    setOpen((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  if (items.length === 0) return <p className="text-sm text-ink-500">No exchanges yet.</p>;

  return (
    <ul className="space-y-1.5">
      {items.map((x) => {
        const canExpand = expandable && (x.documents?.length ?? 0) > 0;
        return (
          <Fragment key={x.id}>
            <li className="flex items-center gap-2 text-sm">
              {canExpand ? (
                <button
                  onClick={() => toggle(x.id)}
                  aria-expanded={open.has(x.id)}
                  aria-label={`Documents for ${x.id}`}
                  className="rounded-md p-0.5 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
                >
                  {open.has(x.id) ? <ChevronDown className="size-3.5" /> : <ChevronRight className="size-3.5" />}
                </button>
              ) : (
                <ArrowLeftRight className="size-3.5 shrink-0 text-ink-300" aria-hidden />
              )}
              <code className="min-w-0 flex-1 truncate font-mono text-xs text-ink-600">{x.id}</code>
              <ExchangeStatusBadge status={x.status} />
              <span className="w-16 shrink-0 text-right text-xs text-ink-400">{timeAgo(x.on)}</span>
            </li>
            {canExpand && open.has(x.id) && (
              <li>
                <ExchangeDocsDrawer documents={x.documents!} />
              </li>
            )}
          </Fragment>
        );
      })}
    </ul>
  );
}

/** Integrations referencing this entity, each linking to its page. */
export function SetupList({ items }: { items: IntegrationSetupRef[] }) {
  if (items.length === 0)
    return <p className="text-sm text-ink-500">Not used by any integration.</p>;
  return (
    <ul className="space-y-1.5">
      {items.map((s) => (
        <li key={s.id} className="flex items-center gap-2.5 text-sm">
          <Workflow className="size-3.5 shrink-0 text-ink-300" aria-hidden />
          <Link
            to={`/subscriptions/${s.id}`}
            className="min-w-0 flex-1 truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
          >
            {s.name}
          </Link>
          <TypeBadge type={s.type} />
        </li>
      ))}
    </ul>
  );
}

/**
 * All integrations, cached hard — pages use it to answer "who uses this
 * property/value/policy?" without extra requests.
 */
export function useIntegrationsCache() {
  return useQuery({
    queryKey: ["integrations"],
    queryFn: () => api.listIntegrations(),
    staleTime: Infinity,
  });
}

/** Tiny inline list for drill-down drawers. */
export function IntegrationMiniList({
  items,
  emptyText,
}: {
  items: IntegrationInfo[];
  emptyText: string;
}) {
  if (items.length === 0) return <p className="text-[13px] text-ink-500">{emptyText}</p>;
  return (
    <ul className="space-y-1">
      {items.map((s) => (
        <li key={s.id} className="flex items-center gap-2 text-[13px]">
          <Workflow className="size-3 shrink-0 text-ink-400" aria-hidden />
          <Link
            to={`/subscriptions/${s.id}`}
            className="min-w-0 flex-1 truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
          >
            {s.name}
          </Link>
          <Badge>{INTEGRATION_TYPE_LABELS[s.type]}</Badge>
        </li>
      ))}
    </ul>
  );
}
