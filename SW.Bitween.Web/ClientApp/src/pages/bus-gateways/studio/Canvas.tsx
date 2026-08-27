import { Fragment } from "react";
import { Link } from "react-router";
import { ArrowUpRight, Radio, Workflow } from "lucide-react";
import type { SubscriptionRow } from "../../../api";
import { PanZoomCanvas } from "../../../components/ui/PanZoomCanvas";
import { Connector, StageNode, type StageFace } from "../../subscriptions/studio/StageRail";
import { faceOf, type AdapterCatalogs } from "../../subscriptions/studio/faces";
import { NEW_SUBSCRIPTION_ID } from "../../subscriptions/studio/model";
import {
  BUS_NODES,
  HEALTH_DOT,
  HEALTH_LABEL,
  HEALTH_TITLE,
  nodeDirty,
  routeHealth,
  type BusDestination,
  type BusListener,
  type BusNodeId,
  type SubscriptionDraft,
} from "./model";

/**
 * One subscription in the response chain. Only `active` is expanded into editable
 * nodes; the rest are summaries — three hops fully expanded is fifteen nodes, and
 * nobody reads fifteen nodes.
 */
export interface Hop {
  subscriptionId: number;
  name: string;
  /** Present once the subscription's detail has loaded. */
  draft: SubscriptionDraft | null;
  saved: SubscriptionDraft | null;
  row: SubscriptionRow | undefined;
  /** Set on the response message this hop publishes, if any. */
  destination: BusDestination | null;
}

/**
 * The gateway's data path for one route, left to right.
 *
 * Pan and zoom live in `PanZoomCanvas`, shared with the flow map — the two diagrams
 * have to behave identically to feel like one tool.
 */
export function Canvas({
  routeFace,
  informationTypeId,
  hops,
  activeHop,
  onSelectHop,
  selectedNode,
  onSelectNode,
  catalogs,
  subscriptionNames,
  onOpenListener,
  routeChosen,
  /** Room reserved on the left for the floating route panel, in rem. */
  gutterRem,
  /** Changes when the route changes — the view resets to the start of the path. */
  resetKey,
}: {
  routeFace: Omit<StageFace, "id">;
  informationTypeId: number;
  hops: Hop[];
  activeHop: number;
  onSelectHop: (index: number) => void;
  selectedNode: BusNodeId | null;
  onSelectNode: (node: BusNodeId | null) => void;
  catalogs: AdapterCatalogs;
  subscriptionNames: { id: number; name: string }[];
  onOpenListener: (listener: BusListener) => void;
  /** False while a new route still has no subscription — there is no chain to draw yet. */
  routeChosen: boolean;
  gutterRem: number;
  resetKey: string;
}) {
  const pick = (node: BusNodeId) => onSelectNode(selectedNode === node ? null : node);

  return (
    <PanZoomCanvas
      resetKey={resetKey}
      fitLabel="Fit the whole path"
      contentClassName="flex min-h-full min-w-max flex-col"
    >
        <div
          className="flex flex-1 items-center py-7 pr-6"
          // Reserved so the floating route panel never covers the start of the path.
          style={{ paddingLeft: `${gutterRem + 1.5}rem` }}
        >
        <StageNode
          face={routeFace}
          label={BUS_NODES.route.label}
          icon={BUS_NODES.route.icon}
          selected={selectedNode === "route"}
          onSelect={() => pick("route")}
          className="w-64"
        />

        {!routeChosen ? (
          <>
            <Connector />
            <div className="flex w-64 shrink-0 items-center rounded-xl border border-dashed border-ink-300 bg-ink-50/60 px-4 py-3.5">
              <p className="text-[13px] text-ink-500">
                Pick the subscription this route runs — it appears here, with its whole pipeline.
              </p>
            </div>
          </>
        ) : (
          <HopChain
            hops={hops}
            index={0}
            activeHop={activeHop}
            onSelectHop={onSelectHop}
            selectedNode={selectedNode}
            onSelectNode={pick}
            catalogs={catalogs}
            subscriptionNames={subscriptionNames}
            onOpenListener={onOpenListener}
          />
        )}
        </div>

        <p
          className="shrink-0 pr-6 pb-5 text-[12px] text-ink-400"
          style={{ paddingLeft: `${gutterRem + 1.5}rem` }}
        >
          Every message of this type is offered to all of this gateway's routes —{" "}
          <Link to={`/information-types/${informationTypeId}`} className="hover:text-ink-600 hover:underline">
            see the information type
          </Link>{" "}
          for the properties a filter can test.
        </p>
    </PanZoomCanvas>
  );
}

/**
 * One hop and everything downstream of it.
 *
 * Recursive, and forked rather than flattened into a single row: a Response can
 * both chain into another subscription *and* publish on the bus, and those two are
 * siblings. Drawn one after the other, the bus card read as if it fed the chained
 * subscription, which is a different — and wrong — story about the data path.
 */
function HopChain({
  hops,
  index,
  activeHop,
  onSelectHop,
  selectedNode,
  onSelectNode,
  catalogs,
  subscriptionNames,
  onOpenListener,
}: {
  hops: Hop[];
  index: number;
  activeHop: number;
  onSelectHop: (index: number) => void;
  selectedNode: BusNodeId | null;
  onSelectNode: (node: BusNodeId) => void;
  catalogs: AdapterCatalogs;
  subscriptionNames: { id: number; name: string }[];
  onOpenListener: (listener: BusListener) => void;
}) {
  const hop = hops[index];
  if (!hop) return null;
  const hasNext = index + 1 < hops.length;
  // Depth cap: three hops is already more chain than a real gateway has, and a
  // fourth would push the route itself off the screen.
  const cappedAt = !hasNext && hop.draft?.responseSubscriptionId != null;
  const forks = hasNext || cappedAt || !!hop.destination;

  return (
    <>
      <Connector />
      {index === activeHop ? (
        <ExpandedHop
          hop={hop}
          selectedNode={selectedNode}
          onSelectNode={onSelectNode}
          catalogs={catalogs}
          subscriptionNames={subscriptionNames}
        />
      ) : (
        <CollapsedHop
          hop={hop}
          // "Then" only ever means downstream of a response. The route's own
          // subscription is not a "then", however it happens to be drawn.
          label={index === 0 ? BUS_NODES.subscription.label : "Then"}
          onOpen={() => onSelectHop(index)}
        />
      )}

      {forks && (
        <div className="flex shrink-0 flex-col justify-center gap-3 self-center">
          {hasNext && (
            <div className="flex items-stretch">
              <HopChain
                hops={hops}
                index={index + 1}
                activeHop={activeHop}
                onSelectHop={onSelectHop}
                selectedNode={selectedNode}
                onSelectNode={onSelectNode}
                catalogs={catalogs}
                subscriptionNames={subscriptionNames}
                onOpenListener={onOpenListener}
              />
            </div>
          )}
          {cappedAt && (
            <div className="flex items-stretch">
              <Connector />
              <Link
                to={`/subscriptions/${hop.draft!.responseSubscriptionId}`}
                className="flex w-52 shrink-0 flex-col justify-center rounded-xl border border-dashed border-ink-300 bg-ink-50/60 px-4 py-3.5 hover:border-ink-400"
              >
                <span className="text-[13px] font-medium text-ink-700">The chain continues</span>
                <span className="mt-1 inline-flex items-center gap-1 text-[12px] text-crimson-700">
                  Open the next subscription <ArrowUpRight className="size-3" />
                </span>
              </Link>
            </div>
          )}
          {hop.destination && (
            <div className="flex items-stretch">
              <Connector />
              <BusDestinationCard
                messageTypeName={hop.draft?.responseMessageTypeName ?? ""}
                destination={hop.destination}
                onOpenListener={onOpenListener}
              />
            </div>
          )}
        </div>
      )}
    </>
  );
}

/** The active subscription: its own pipeline, editable, inside one labelled frame. */
function ExpandedHop({
  hop,
  selectedNode,
  onSelectNode,
  catalogs,
  subscriptionNames,
}: {
  hop: Hop;
  selectedNode: BusNodeId | null;
  onSelectNode: (node: BusNodeId) => void;
  catalogs: AdapterCatalogs;
  subscriptionNames: { id: number; name: string }[];
}) {
  const health = routeHealth(hop.row);
  const isNew = hop.subscriptionId === NEW_SUBSCRIPTION_ID;
  const headerDirty = hop.draft && hop.saved ? nodeDirty("subscription", hop.draft, hop.saved) : false;

  if (!hop.draft)
    return (
      <div className="flex w-72 shrink-0 items-center rounded-2xl border border-ink-200 bg-white/60 px-4 py-3.5">
        <p className="text-[13px] text-ink-500">Loading {hop.name}…</p>
      </div>
    );

  // Typed as the subscription studio's own stage ids: these three nodes *are* its
  // stages, and `faceOf` is what describes them.
  const stages = ["transformation", "delivery", "response"] as const;

  return (
    <div className="shrink-0 rounded-2xl border-2 border-ink-200 bg-white/70 p-3">
      <button
        type="button"
        onClick={() => onSelectNode("subscription")}
        aria-current={selectedNode === "subscription" ? "true" : undefined}
        className={`mb-3 flex w-full items-center gap-2 rounded-lg px-2 py-1.5 text-left ${
          selectedNode === "subscription" ? "bg-crimson-50 ring-2 ring-crimson-200" : "hover:bg-ink-50"
        }`}
      >
        <Workflow className="size-4 shrink-0 text-ink-500" aria-hidden />
        <span className="min-w-0 flex-1 truncate text-[14px] font-semibold text-ink-900" title={hop.name}>
          {hop.draft.name.trim() || hop.name}
        </span>
        {headerDirty && (
          <span className="size-2 shrink-0 rounded-full bg-warn-700" title="Unsaved changes" />
        )}
        {!hop.draft.enabled && (
          <span className="shrink-0 rounded-md bg-ink-100 px-1.5 py-0.5 text-[11px] font-medium text-ink-700">
            Disabled
          </span>
        )}
        {/* Nothing has run yet, so there is no health to report. */}
        {!isNew && (
          <span
            className={`size-1.5 shrink-0 rounded-full ${HEALTH_DOT[health]}`}
            title={HEALTH_TITLE[health]}
            aria-label={HEALTH_LABEL[health]}
          />
        )}
      </button>

      <div className="flex items-stretch">
        {stages.map((node, i) => (
          <Fragment key={node}>
            {i > 0 && <Connector />}
            <StageNode
              face={faceOf(node, {
                type: "BusGateway",
                draft: hop.draft!,
                saved: hop.saved ?? undefined,
                catalogs,
                subscriptionNames,
                unsaved: isNew,
              })}
              label={BUS_NODES[node].label}
              icon={BUS_NODES[node].icon}
              selected={selectedNode === node}
              onSelect={() => onSelectNode(node)}
              className="w-56"
            />
          </Fragment>
        ))}
      </div>
    </div>
  );
}

/** A subscription in the chain, rolled up. Click to make it the editable one. */
function CollapsedHop({ hop, label, onOpen }: { hop: Hop; label: string; onOpen: () => void }) {
  const health = routeHealth(hop.row);
  const d = hop.draft;
  const strip: { key: string; label: string; set: boolean }[] = [
    { key: "t", label: "Transformation", set: !!d?.mapperId },
    { key: "d", label: "Delivery", set: !!d?.handlerId },
    {
      key: "r",
      label: "Response",
      set: !!(d && (d.responseSubscriptionId !== null || d.responseMessageTypeName)),
    },
  ];

  return (
    <button
      type="button"
      onClick={onOpen}
      title={`Configure ${hop.name}`}
      className="w-60 shrink-0 cursor-pointer rounded-xl border border-ink-200 bg-white px-4 py-3.5 text-left shadow-sm hover:shadow-md"
    >
      <div className="flex items-center gap-2">
        <span className="grid size-7 shrink-0 place-items-center rounded-lg bg-ink-100 text-ink-500">
          <Workflow className="size-4" aria-hidden />
        </span>
        <span className="min-w-0 flex-1 truncate text-[11px] font-semibold tracking-wide text-ink-400 uppercase">
          {label}
        </span>
        <span
          className={`size-1.5 shrink-0 rounded-full ${HEALTH_DOT[health]}`}
          title={HEALTH_TITLE[health]}
          aria-label={HEALTH_LABEL[health]}
        />
      </div>
      <p className="mt-2.5 truncate text-[15px] font-semibold text-ink-800" title={hop.name}>
        {hop.name}
      </p>
      {/* Filled means configured, not healthy: per-step health is something we
          don't have, and colouring these by outcome would be inventing it. */}
      <span className="mt-2 flex items-center gap-1.5">
        {strip.map((s) => (
          <span
            key={s.key}
            title={`${s.label}: ${s.set ? "configured" : "not set"}`}
            className={`h-1 w-7 rounded-full ${s.set ? "bg-ink-500" : "bg-ink-200"}`}
          />
        ))}
        <span className="ml-1 text-[11px] text-crimson-700">Configure</span>
      </span>
    </button>
  );
}

/**
 * What a published response reaches. Its own card because it is a fan-out, not a
 * step: one message can wake every route bound to that information type, across
 * gateways.
 */
function BusDestinationCard({
  messageTypeName,
  destination,
  onOpenListener,
}: {
  messageTypeName: string;
  destination: BusDestination;
  onOpenListener: (listener: BusListener) => void;
}) {
  const { informationType, listeners } = destination;

  return (
    <div className="flex w-72 shrink-0 flex-col rounded-xl border border-ink-200 bg-white px-4 py-3.5 shadow-sm">
      <div className="flex items-center gap-2">
        <span className="grid size-7 shrink-0 place-items-center rounded-lg bg-ink-100 text-ink-500">
          <Radio className="size-4" aria-hidden />
        </span>
        <span className="min-w-0 flex-1 truncate text-[11px] font-semibold tracking-wide text-ink-400 uppercase">
          On the bus
        </span>
      </div>
      <p className="mt-2.5 truncate font-mono text-[13px] font-semibold text-ink-800" title={messageTypeName}>
        {messageTypeName}
      </p>

      {!informationType ? (
        <p className="mt-2 text-[12px] text-warn-700">
          No information type carries this message name, so nothing is listening for it yet.
        </p>
      ) : listeners.length === 0 ? (
        <p className="mt-2 text-[12px] text-ink-500">
          Carried by {informationType.code ?? informationType.name}. No route picks it up yet.
        </p>
      ) : (
        <>
          <p className="mt-2 text-[12px] text-ink-500">
            Picked up by {listeners.length} route{listeners.length === 1 ? "" : "s"} on{" "}
            {informationType.code ?? informationType.name}:
          </p>
          <ul className="mt-1.5 max-h-40 space-y-1 overflow-y-auto">
            {listeners.map((l) => (
              <li key={`${l.gatewayId}-${l.routeId}`}>
                <button
                  type="button"
                  onClick={() => onOpenListener(l)}
                  className="block w-full rounded-lg px-2 py-1 text-left hover:bg-ink-50"
                >
                  <span className="block truncate text-[12px] font-medium text-ink-800">
                    {l.subscriptionName}
                  </span>
                  <span className="block truncate text-[11px] text-ink-400" title={l.condition}>
                    {l.gatewayName} · {l.condition}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  );
}
