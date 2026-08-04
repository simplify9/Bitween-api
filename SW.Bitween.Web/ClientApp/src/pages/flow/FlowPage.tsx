import { useMemo, useState } from "react";
import { Link } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, ChevronDown, RotateCcw } from "lucide-react";
import { api } from "../../api";
import { useSessionCan } from "../../auth/guards";
import { useIntegrationsCache } from "../../components/config/shared";
import { EmptyState, LoadingBlock } from "../../components/ui/basics";
import { FlowLegend, FlowMap } from "./FlowMap";
import { buildFlowGraph } from "./model";
import { layoutFlow } from "./layout";

/**
 * How data moves between gateways, on one surface.
 *
 * Deliberately read-only, and deliberately not the route studio. The two answer
 * different questions at different altitudes: this one is "where does a response
 * end up", zoomed out far enough that a whole installation fits and cycles are
 * visible; the studio is "what exactly does this route do", zoomed in far enough
 * to hold forms. One canvas trying to do both is too dense to configure in and too
 * detailed to read as a map, so clicking anything here hands over to the surface
 * that owns it.
 */
export function FlowPage() {
  const canSeeApi = useSessionCan("api-gateways.view");
  const canSeeBus = useSessionCan("bus-gateways.view");

  const integrations = useIntegrationsCache();
  const informationTypes = useQuery({
    queryKey: ["information-types"],
    queryFn: () => api.listInformationTypes(),
  });
  const apiGateways = useQuery({
    queryKey: ["api-gateways"],
    queryFn: () => api.listApiGateways(),
    enabled: canSeeApi,
  });
  const busGateways = useQuery({
    queryKey: ["bus-gateways"],
    queryFn: () => api.listBusGateways(),
    enabled: canSeeBus,
  });

  const graph = useMemo(
    () =>
      buildFlowGraph({
        apiGateways: apiGateways.data ?? [],
        busGateways: busGateways.data ?? [],
        informationTypes: informationTypes.data ?? [],
        integrations: integrations.data ?? [],
      }),
    [apiGateways.data, busGateways.data, informationTypes.data, integrations.data],
  );

  // Laid out here rather than inside the map: the page's own loop banner needs to
  // know whether a cycle was found before the canvas is drawn.
  const layout = useMemo(() => layoutFlow(graph), [graph]);

  const pending =
    integrations.isPending ||
    informationTypes.isPending ||
    (canSeeApi && apiGateways.isPending) ||
    (canSeeBus && busGateways.isPending);

  if (pending) return <LoadingBlock label="Reading the topology…" />;

  const attention = graph.nodes.filter((n) => n.warning);
  const gateways = graph.nodes.filter((n) => n.kind === "busGateway" || n.kind === "apiGateway").length;
  const messages = graph.nodes.filter((n) => n.kind === "message").length;

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <header className="flex flex-wrap items-end justify-between gap-x-6 gap-y-2 px-4 pt-4 pb-3 sm:px-6">
        <div>
          <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">Flow map</h1>
          <p className="mt-0.5 text-sm text-ink-500">
            Every gateway, and the bus messages that carry work between them. Click anything to open it.
          </p>
        </div>
        <div className="flex flex-col items-end gap-1.5">
          <p className="text-[12px] text-ink-400 tabular-nums">
            {gateways} {gateways === 1 ? "gateway" : "gateways"} · {messages}{" "}
            {messages === 1 ? "message" : "messages"}
            {graph.omitted > 0 && (
              <>
                {" · "}
                <span title="They belong to no gateway and route no response, so they have no place on a flow map.">
                  {graph.omitted} not shown
                </span>
              </>
            )}
          </p>
          <FlowLegend />
        </div>
      </header>

      {layout.looping.size > 0 && (
        <div className="mx-4 mb-3 flex items-start gap-2 rounded-xl border border-danger-300 bg-danger-50 px-3.5 py-2.5 sm:mx-6">
          <RotateCcw className="mt-0.5 size-4 shrink-0 text-danger-700" aria-hidden />
          <p className="text-[13px] text-danger-800">
            <strong className="font-medium">A response comes back round.</strong> One of these paths publishes a
            message that eventually reaches it again, so a single incoming message will keep being reprocessed. The
            loop is drawn in red below.
          </p>
        </div>
      )}

      {attention.length > 0 && <AttentionBanner items={attention} />}

      {graph.nodes.length === 0 ? (
        <div className="px-4 sm:px-6">
          <EmptyState title="Nothing to map yet">
            A flow appears once there is a gateway to draw.{" "}
            <Link to="/bus-gateways" className="font-medium text-crimson-700 hover:underline">
              Add a bus gateway
            </Link>{" "}
            to begin.
          </EmptyState>
        </div>
      ) : (
        <FlowMap layout={layout} />
      )}
    </div>
  );
}

/**
 * The things on the map that look configured and move nothing.
 *
 * Above the canvas rather than floating over it. As a panel it covered whichever
 * node happened to be top-left — and the node it hid was, of course, one of the
 * ones it was warning about. Collapsed by default so the diagram keeps the height:
 * the count alone is enough to make someone open it.
 */
function AttentionBanner({ items }: { items: { id: string; title: string; href: string; warning?: string }[] }) {
  const [open, setOpen] = useState(false);

  return (
    <div className="mx-4 mb-3 overflow-hidden rounded-xl border border-warn-400 bg-warn-100/50 sm:mx-6">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className="flex w-full items-center gap-2 px-3.5 py-2.5 text-left hover:bg-warn-100"
        aria-expanded={open}
      >
        <AlertTriangle className="size-4 shrink-0 text-warn-700" aria-hidden />
        <span className="flex-1 text-[13px] text-ink-800">
          <strong className="font-medium">
            {items.length} {items.length === 1 ? "thing needs" : "things need"} attention.
          </strong>{" "}
          Configuration that looks finished and moves nothing.
        </span>
        <ChevronDown
          className={`size-4 shrink-0 text-ink-500 transition-transform ${open ? "" : "-rotate-90"}`}
          aria-hidden
        />
      </button>
      {open && (
        <ul className="max-h-48 divide-y divide-warn-400/30 overflow-y-auto border-t border-warn-400/40">
          {items.map((n) => (
            <li key={n.id}>
              <Link to={n.href} className="flex items-baseline gap-2 px-3.5 py-2 hover:bg-warn-100">
                <span className="shrink-0 text-[13px] font-medium text-ink-800">{n.title}</span>
                <span className="text-[12px] leading-snug text-ink-600">{n.warning}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
