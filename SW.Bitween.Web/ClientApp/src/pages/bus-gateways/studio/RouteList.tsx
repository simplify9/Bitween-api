import { useMemo, useState } from "react";
import { Plus, Search } from "lucide-react";
import type { BusGatewayRoute, IntegrationRow } from "../../../api";
import { HEALTH_DOT, HEALTH_LABEL, HEALTH_TITLE, conditionText, routeHealth, type RouteDraft } from "./model";

export type Selection = number | "new" | null;

/**
 * The routes, as a panel floating over the canvas.
 *
 * It was a full-height column, and with three routes that was a tall empty block
 * of chrome beside the thing you came to look at. Floating, it is only as tall as
 * it needs to be, and the canvas keeps its whole width — the path reserves a
 * gutter so nothing is ever hidden underneath.
 *
 * Still a list and not a row of chips: all four facts an operator triages on stay
 * on the row — what it matches, whose values it runs with, what it runs, and
 * whether that is healthy — and at a hundred routes a horizontal strip of chips
 * answers none of them.
 */
export function RouteList({
  routes,
  rowsById,
  selected,
  onSelect,
  onAdd,
  pending,
  dirtyRouteId,
  canEdit,
}: {
  routes: BusGatewayRoute[];
  rowsById: Map<number, IntegrationRow>;
  selected: Selection;
  onSelect: (selection: Selection) => void;
  onAdd: () => void;
  /** The unsaved route being added, drawn as a row so it isn't lost off-screen. */
  pending: RouteDraft | null;
  /** Which saved route holds unsaved edits, if any. */
  dirtyRouteId: number | null;
  canEdit: boolean;
}) {
  const [query, setQuery] = useState("");

  const rows = useMemo(() => {
    const decorated = routes.map((r) => {
      const row = rowsById.get(r.integrationId);
      return {
        route: r,
        condition: conditionText(r.matchExpression),
        integrationName: r.integrationName || `#${r.integrationId}`,
        partner: r.partnerName ?? "Any partner",
        health: routeHealth(row),
      };
    });
    const needle = query.trim().toLowerCase();
    if (!needle) return decorated;
    return decorated.filter((d) =>
      [d.condition, d.integrationName, d.partner].some((f) => f.toLowerCase().includes(needle)),
    );
  }, [routes, rowsById, query]);

  return (
    <div className="pointer-events-auto flex max-h-full w-80 flex-col overflow-hidden rounded-xl border border-ink-200 bg-white/95 shadow-md backdrop-blur">
      <div className="flex shrink-0 items-center gap-2 border-b border-ink-100 px-2.5 py-2">
        <div className="relative min-w-0 flex-1">
          <Search className="pointer-events-none absolute top-2.5 left-2.5 size-3.5 text-ink-400" />
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder={`Search ${routes.length} route${routes.length === 1 ? "" : "s"}…`}
            aria-label="Search routes"
            className="w-full rounded-lg border border-ink-200 bg-white py-1.5 pr-2 pl-8 text-[13px] text-ink-800 placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
          />
        </div>
        {canEdit && (
          <button
            type="button"
            onClick={onAdd}
            title="Add a route"
            aria-label="Add a route"
            className="shrink-0 rounded-lg bg-crimson-600 p-1.5 text-white hover:bg-crimson-700"
          >
            <Plus className="size-4" />
          </button>
        )}
      </div>

      {/* Its own scroll, so a hundred routes never push the panel past the canvas. */}
      <div className="min-h-0 flex-1 overflow-y-auto">
        {pending && (
          <Row
            active={selected === "new"}
            onSelect={() => onSelect("new")}
            condition={conditionText(pending.matchExpression)}
            partner={pending.partner === "none" ? "Any partner" : "…"}
            integrationName="New route — not saved"
            health="unknown"
            dirty
          />
        )}

        {rows.map((d) => (
          <Row
            key={d.route.id}
            active={selected === d.route.id}
            onSelect={() => onSelect(d.route.id)}
            condition={d.condition}
            partner={d.partner}
            integrationName={d.integrationName}
            health={d.health}
            dirty={dirtyRouteId === d.route.id}
          />
        ))}

        {rows.length === 0 && !pending && (
          <p className="px-3 py-6 text-[13px] text-ink-500">
            {routes.length === 0
              ? "No routes yet — every message reaching this gateway is ignored."
              : "No route matches that search."}
          </p>
        )}
      </div>
    </div>
  );
}

function Row({
  active,
  onSelect,
  condition,
  partner,
  integrationName,
  health,
  dirty,
}: {
  active: boolean;
  onSelect: () => void;
  condition: string;
  partner: string;
  integrationName: string;
  health: keyof typeof HEALTH_DOT;
  dirty: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onSelect}
      aria-current={active ? "true" : undefined}
      className={`relative block w-full border-b border-ink-100 px-3 py-2 text-left last:border-b-0 ${
        active ? "bg-ink-100/70" : "hover:bg-ink-50"
      }`}
    >
      {active && <span className="absolute inset-y-0 left-0 w-0.5 bg-crimson-600" aria-hidden />}
      <span className="flex items-center gap-1.5">
        <span
          className={`size-1.5 shrink-0 rounded-full ${HEALTH_DOT[health]}`}
          title={HEALTH_TITLE[health]}
          aria-label={HEALTH_LABEL[health]}
        />
        <span
          className={`min-w-0 flex-1 truncate text-[13px] font-medium ${active ? "text-ink-900" : "text-ink-800"}`}
          title={condition}
        >
          {condition}
        </span>
        {dirty && (
          <span
            className="size-2 shrink-0 rounded-full bg-warn-700"
            title="Unsaved changes"
            aria-label="Unsaved changes"
          />
        )}
      </span>
      <span className="mt-1 flex items-baseline gap-1.5 pl-3 text-[12px]">
        <span className="shrink-0 truncate text-ink-400" title={partner}>
          {partner}
        </span>
        <span className="shrink-0 text-ink-300" aria-hidden>
          →
        </span>
        <span className="min-w-0 flex-1 truncate text-ink-600" title={integrationName}>
          {integrationName}
        </span>
      </span>
    </button>
  );
}
