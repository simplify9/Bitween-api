import { Link } from "react-router";
import { Clock, Globe, Radio, RotateCcw, Send, Workflow, type LucideIcon } from "lucide-react";
import { PanZoomCanvas } from "../../components/ui/PanZoomCanvas";
import { NODE_H, NODE_W, type FlowLayout, type PlacedEdge, type PlacedNode } from "./layout";
import { isEntryPoint, type FlowNodeKind } from "./model";

const KINDS: Record<FlowNodeKind, { label: string; icon: LucideIcon }> = {
  message: { label: "Bus message", icon: Send },
  busGateway: { label: "Bus gateway", icon: Radio },
  apiGateway: { label: "API gateway", icon: Globe },
  job: { label: "Scheduled job", icon: Clock },
  subscription: { label: "Subscription", icon: Workflow },
};

/**
 * Edges are drawn in structural greys and problems in danger red, rather than
 * everything in the brand colour. On a diagram, colour is the strongest signal
 * available, and spending it on decoration leaves nothing to say "look here".
 */
const EDGE_TONE: Record<"bus" | "handsOff" | "loop", { stroke: string; dash?: string; marker: string }> = {
  bus: { stroke: "var(--color-ink-400)", marker: "flow-arrow-bus" },
  handsOff: { stroke: "var(--color-ink-300)", dash: "5 4", marker: "flow-arrow-hand" },
  loop: { stroke: "var(--color-danger-600)", dash: "5 4", marker: "flow-arrow-loop" },
};

const toneOf = (e: PlacedEdge) => (e.loop ? "loop" : e.kind === "handsOff" ? "handsOff" : "bus");

export function FlowMap({ layout }: { layout: FlowLayout }) {
  const { nodes, edges, width, height, looping } = layout;

  return (
    <PanZoomCanvas
      resetKey={`${nodes.length}:${edges.length}`}
      fitLabel="Fit the whole map"
      contentClassName="relative"
      contentStyle={{ width, height, minWidth: "100%" }}
    >
      <svg width={width} height={height} className="absolute top-0 left-0" aria-hidden="true">
        <defs>
          {Object.values(EDGE_TONE).map((tone) => (
            <marker
              key={tone.marker}
              id={tone.marker}
              viewBox="0 0 10 10"
              refX="9"
              refY="5"
              markerWidth="5"
              markerHeight="5"
              orient="auto-start-reverse"
            >
              <path d="M 0 0 L 10 5 L 0 10 z" fill={tone.stroke} />
            </marker>
          ))}
        </defs>
        {edges.map((e) => {
          const tone = EDGE_TONE[toneOf(e)];
          return (
            <path
              key={e.id}
              d={e.path}
              fill="none"
              stroke={tone.stroke}
              strokeWidth={1.5}
              strokeDasharray={tone.dash}
              markerEnd={`url(#${tone.marker})`}
            />
          );
        })}
      </svg>

      {nodes.map((n) => (
        <NodeCard key={n.id} node={n} onLoop={looping.has(n.id)} entryPoint={isEntryPoint(n.id, edges)} />
      ))}

      {/* The same relationships in words, for anyone who can't see the arrows. */}
      <ul className="sr-only">
        {edges.map((e) => {
          const from = nodes.find((n) => n.id === e.from)?.title;
          const to = nodes.find((n) => n.id === e.to)?.title;
          const verb =
            e.kind === "listens" ? "is picked up by" : e.kind === "publishes" ? "publishes" : "hands its response to";
          return (
            <li key={e.id}>
              {from} {verb} {to}
              {e.loop ? " — closing a loop" : ""}
            </li>
          );
        })}
      </ul>
    </PanZoomCanvas>
  );
}

function NodeCard({
  node,
  onLoop,
  entryPoint,
}: {
  node: PlacedNode;
  onLoop: boolean;
  /** Nothing inside Bitween feeds it, so it starts the path. */
  entryPoint: boolean;
}) {
  const { label, icon: Icon } = KINDS[node.kind];
  const message = node.kind === "message";
  // Everything the card had to truncate, in full — the code and the detail line
  // are exactly what gets cut at 216px.
  const summary = [`${label}: ${node.title}`, node.code, node.detail].filter(Boolean).join(" · ");
  // The loop icon and the warning dot are otherwise unexplained to a mouse user —
  // only a screen reader gets their `aria-label`, since a card can be hovered
  // anywhere and this is the one title the browser will show either way.
  const extra = [
    onLoop && "on a loop — a message from here eventually feeds back into itself",
    node.warning,
  ]
    .filter(Boolean)
    .join(" — ");
  const border = onLoop
    ? "border-danger-300 bg-danger-50"
    : node.warning
      ? "border-warn-400 bg-white"
      : message
        ? "border-dashed border-ink-300 bg-ink-50"
        : "border-ink-200 bg-white hover:border-ink-300";

  return (
    <Link
      to={node.href}
      title={extra ? `${summary} — ${extra}` : summary}
      style={{ left: node.x, top: node.y, width: NODE_W, height: NODE_H }}
      className={`absolute flex flex-col justify-center overflow-hidden rounded-xl border px-3.5 py-3 shadow-sm transition-shadow hover:shadow-md ${border}`}
    >
      <div className="flex items-center gap-2">
        <span
          className={`grid size-6 shrink-0 place-items-center rounded-lg ${
            message ? "bg-ink-200 text-ink-600" : "bg-ink-100 text-ink-500"
          }`}
        >
          <Icon className="size-3.5" aria-hidden />
        </span>
        <span className="min-w-0 flex-1 truncate text-[10px] font-semibold tracking-wide text-ink-400 uppercase">
          {label}
        </span>
        {onLoop ? (
          <RotateCcw className="size-3.5 shrink-0 text-danger-600" aria-label="On a loop" />
        ) : node.warning ? (
          <span className="size-2 shrink-0 rounded-full bg-warn-400" aria-label="Needs attention" />
        ) : null}
      </div>

      <p className="mt-2 truncate text-[14px] font-semibold text-ink-800">{node.title}</p>
      <p className="mt-0.5 truncate text-[11px] text-ink-400">
        {node.code && <code className="font-mono">{node.code}</code>}
        {node.code && node.detail && " · "}
        {/* A message with no publisher isn't broken — most traffic starts outside. */}
        {message && entryPoint ? "published from outside" : node.detail}
      </p>
    </Link>
  );
}

/** What the three line styles mean. Small, because a legend nobody needs is clutter. */
export function FlowLegend() {
  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1.5 text-[11px] text-ink-500">
      {(
        [
          ["bus", "on the bus"],
          ["handsOff", "handed straight on"],
          ["loop", "loops back"],
        ] as const
      ).map(([tone, text]) => (
        <span key={tone} className="flex items-center gap-1.5">
          <svg width="22" height="8" aria-hidden>
            <line
              x1="0"
              y1="4"
              x2="22"
              y2="4"
              stroke={EDGE_TONE[tone].stroke}
              strokeWidth={1.5}
              strokeDasharray={EDGE_TONE[tone].dash}
            />
          </svg>
          {text}
        </span>
      ))}
    </div>
  );
}
