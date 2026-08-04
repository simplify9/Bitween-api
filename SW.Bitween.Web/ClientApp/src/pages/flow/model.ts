import type { ApiGatewayRow, BusGatewayRow, InformationTypeRow, IntegrationInfo } from "../../api";

/**
 * The bus topology as a graph.
 *
 * Two questions a route studio structurally cannot answer, because both span
 * gateways: where does a published response actually end up, and does anything come
 * back round. Both fall out of four list calls with no new endpoint, because
 * publishing is resolved by *name*: `BusService` maps a message type name to the one
 * information type carrying it (the column is unique), and `FilterService` then
 * offers that message to every bus-gateway route bound to the type — on any gateway.
 *
 * Messages are nodes rather than edge labels on purpose. Fan-out is the whole point:
 * one message reaching five gateways is one node with five arrows, which reads at a
 * glance, where five separately-labelled arrows read as five unrelated facts. It also
 * makes the silent failure visible — a message node with nothing leaving it is a
 * response published to nobody at all.
 */

export type FlowNodeKind = "apiGateway" | "busGateway" | "job" | "integration" | "message";

export interface FlowNode {
  id: string;
  kind: FlowNodeKind;
  title: string;
  /** What it holds, in a few words: "2 routes", "3 partners posting in". */
  detail: string;
  /** Monospace identity: a url name, an information type code, a bus message name. */
  code?: string;
  href: string;
  /**
   * Why this node needs attention. Shown on the card, not only as a tooltip — every
   * one of these is a configuration that looks finished and moves nothing.
   */
  warning?: string;
}

export type FlowEdgeKind = "publishes" | "listens" | "handsOff";

export interface FlowEdge {
  id: string;
  from: string;
  to: string;
  kind: FlowEdgeKind;
}

export interface FlowGraph {
  nodes: FlowNode[];
  edges: FlowEdge[];
  /** Integrations with no place on the map: no gateway runs them, no response leaves them. */
  omitted: number;
}

const JOB_TYPES = new Set(["Receiving", "Aggregation"]);

const routeWord = (n: number) => `${n} ${n === 1 ? "route" : "routes"}`;
const partnerWord = (n: number) => `${n} ${n === 1 ? "partner" : "partners"}`;

export function buildFlowGraph({
  apiGateways,
  busGateways,
  informationTypes,
  integrations,
}: {
  apiGateways: ApiGatewayRow[];
  busGateways: BusGatewayRow[];
  informationTypes: InformationTypeRow[];
  integrations: IntegrationInfo[];
}): FlowGraph {
  const nodes = new Map<string, FlowNode>();
  const edges: FlowEdge[] = [];
  const warn = (id: string, text: string) => {
    const node = nodes.get(id);
    if (node) node.warning = node.warning ? `${node.warning} ${text}` : text;
  };

  const integrationById = new Map(integrations.map((i) => [i.id, i]));
  const typeById = new Map(informationTypes.map((t) => [t.id, t]));

  /** Which node runs an integration. A route or an attachment is drawn as its gateway. */
  const runBy = new Map<number, string>();

  // ——— Gateways always appear, even with nothing attached: an idle gateway is a
  // thing you need to be able to find, and the map is the inventory of them.
  for (const g of apiGateways) {
    nodes.set(`apigw:${g.id}`, {
      id: `apigw:${g.id}`,
      kind: "apiGateway",
      title: g.name,
      detail: `${partnerWord(g.attachments.length)} posting in`,
      code: g.urlName,
      href: `/api-gateways/${g.id}`,
      ...(g.attachments.length === 0 ? { warning: "No partner is attached, so nothing can post to it." } : {}),
    });
    for (const a of g.attachments) runBy.set(a.integrationId, `apigw:${g.id}`);
  }

  for (const g of busGateways) {
    const type = typeById.get(g.informationTypeId);
    nodes.set(`busgw:${g.id}`, {
      id: `busgw:${g.id}`,
      kind: "busGateway",
      title: g.name,
      detail: routeWord(g.routes.length),
      // From the information type, not `g.informationTypeCode` — the bus gateway
      // list endpoint sends `documentName`, so that field holds the name, and the
      // gateway cards would have shown a name where every other card shows a code.
      code: type?.code || g.informationTypeCode,
      href: `/bus-gateways/${g.id}`,
      ...(g.routes.length === 0 ? { warning: "It has no routes, so every message it receives is dropped." } : {}),
    });
    for (const r of g.routes) runBy.set(r.integrationId, `busgw:${g.id}`);
  }

  // ——— Every bus-enabled information type is a message node. Listed even when
  // untouched: one that nothing publishes and nothing listens for is a mistake
  // that has no other place to show up.
  const messageByName = new Map<string, string>();
  for (const t of informationTypes) {
    if (!t.busEnabled || !t.busMessageTypeName) continue;
    const id = `msg:${t.id}`;
    nodes.set(id, {
      id,
      kind: "message",
      title: t.busMessageTypeName,
      detail: t.name,
      code: t.code,
      href: `/information-types/${t.id}`,
    });
    messageByName.set(t.busMessageTypeName.toLowerCase(), id);
  }

  for (const g of busGateways) {
    const type = typeById.get(g.informationTypeId);
    const message = type?.busMessageTypeName ? messageByName.get(type.busMessageTypeName.toLowerCase()) : undefined;
    if (message) edges.push({ id: `listens:${g.id}`, from: message, to: `busgw:${g.id}`, kind: "listens" });
    // Bus can be switched off on a type after a gateway was bound to it, and then
    // the gateway is unreachable while still looking configured.
    else warn(`busgw:${g.id}`, `Its information type is not on the bus, so no message can reach it.`);
  }

  /** A node for an integration no gateway runs — reached only through a response. */
  const standalone = (id: number): string | null => {
    const i = integrationById.get(id);
    if (!i) return null;
    const kind: FlowNodeKind = JOB_TYPES.has(i.type) ? "job" : "integration";
    const nodeId = `${kind === "job" ? "job" : "int"}:${id}`;
    if (!nodes.has(nodeId))
      nodes.set(nodeId, {
        id: nodeId,
        kind,
        title: i.name,
        detail: typeById.get(i.informationTypeId)?.name ?? "",
        code: typeById.get(i.informationTypeId)?.code,
        href: `/subscriptions/${id}`,
      });
    return nodeId;
  };

  for (const i of integrations) {
    if (i.responseMessageTypeName === null && i.responseIntegrationId === null) continue;
    const from = runBy.get(i.id) ?? standalone(i.id);
    if (from === null) continue;

    if (i.responseMessageTypeName !== null && i.responseMessageTypeName !== "") {
      const to = messageByName.get(i.responseMessageTypeName.toLowerCase());
      if (to) edges.push({ id: `pub:${i.id}`, from, to, kind: "publishes" });
      // The hazard `ResponseFields` warns about, caught after the fact: a name no
      // information type carries is published to nobody, and never errors.
      else warn(from, `${i.name} publishes “${i.responseMessageTypeName}”, which no information type carries.`);
    }

    if (i.responseIntegrationId !== null) {
      const to = runBy.get(i.responseIntegrationId) ?? standalone(i.responseIntegrationId);
      // Same node on both ends means one of a gateway's routes hands off to another
      // of its own — real, but a loop drawn onto itself; its studio shows the chain.
      if (to !== null && to !== from) edges.push({ id: `hand:${i.id}`, from, to, kind: "handsOff" });
    }
  }

  // A message nothing picks up. Distinct from one nothing publishes, which is
  // normal — plenty of traffic originates outside Bitween.
  const hasListener = new Set(edges.filter((e) => e.kind === "listens").map((e) => e.from));
  for (const node of nodes.values())
    if (node.kind === "message" && !hasListener.has(node.id))
      node.warning = "No gateway listens for this, so anything published under it is dropped.";

  const omitted = integrations.filter(
    (i) => !runBy.has(i.id) && !nodes.has(`int:${i.id}`) && !nodes.has(`job:${i.id}`),
  ).length;

  return { nodes: [...nodes.values()], edges, omitted };
}

/** Whether a message node has anything publishing into it, for its subtitle. */
export const isEntryPoint = (id: string, edges: FlowEdge[]) => !edges.some((e) => e.to === id);
