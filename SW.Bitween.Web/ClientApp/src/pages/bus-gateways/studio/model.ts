import { Filter, Workflow, type LucideIcon } from "lucide-react";
import type {
  BusGatewayRoute,
  BusGatewayRow,
  InformationTypeRow,
  IntegrationRow,
  MatchGroup,
} from "../../../api";
import { matchSummary } from "../../../lib/match";
import type { StageFace } from "../../integrations/studio/StageRail";
import { STAGES, type StageId } from "../../integrations/studio/stages";
import { stageDirty, type Draft as IntegrationDraft } from "../../integrations/studio/model";

export type { IntegrationDraft };

/**
 * The studio's nodes. Three of them are an integration's own pipeline stages and
 * keep their names from the integration studio, because they are the same thing
 * — a route that opened "Delivery" and an integration page that opened
 * "Delivery" have to be editing the same field.
 *
 * `validation` is absent on purpose: `RunValidator` is only reached from the
 * partner-key API path, so a validator on a bus-gateway subscription is stored
 * and never run. See the note in the integration studio's `stages.ts`.
 */
export type BusNodeId =
  | "route"
  | "integration"
  | Extract<StageId, "transformation" | "delivery" | "response">;

export const BUS_NODES: Record<BusNodeId, { label: string; description: string; icon: LucideIcon }> = {
  route: {
    label: "Matches when",
    description: "Which of those messages this route picks up, and whose values it runs with.",
    icon: Filter,
  },
  integration: {
    label: "Integration",
    description: "Its name, whether it runs at all, and which lane and retry policy it runs under.",
    icon: Workflow,
  },
  transformation: STAGES.transformation,
  delivery: STAGES.delivery,
  response: STAGES.response,
};

/** Which record a node writes to — the studio saves the two separately. */
export const OWNER: Record<BusNodeId, "route" | "integration"> = {
  route: "route",
  integration: "integration",
  transformation: "integration",
  delivery: "integration",
  response: "integration",
};

/**
 * Which integration fields each node owns, so a node can carry an unsaved dot.
 * The three pipeline nodes defer to the integration studio's own map — one list
 * per field, wherever the field is edited.
 */
export const INTEGRATION_NODE_FIELDS: Partial<Record<BusNodeId, (keyof IntegrationDraft)[]>> = {
  integration: ["name", "enabled", "workGroupId", "retryPolicyId"],
};

export const nodeDirty = (
  node: BusNodeId,
  draft: IntegrationDraft,
  saved: IntegrationDraft,
): boolean => {
  const own = INTEGRATION_NODE_FIELDS[node];
  if (own) return own.some((f) => JSON.stringify(draft[f]) !== JSON.stringify(saved[f]));
  if (node === "transformation" || node === "delivery" || node === "response")
    return stageDirty(node, draft, saved);
  return false;
};

/** The three questions a route answers. `partner` mirrors the pickers: "none" is a real answer. */
export interface RouteDraft {
  matchExpression: MatchGroup | null;
  partner: number | "none";
  integrationId: number | null;
}

export const routeDraftOf = (r: BusGatewayRoute): RouteDraft => ({
  matchExpression: structuredClone(r.matchExpression),
  partner: r.partnerId ?? "none",
  integrationId: r.integrationId,
});

/**
 * A route being added. It exists only in the studio's state until saved, which is
 * what lets the canvas draw it exactly like a real one — a modal asking the same
 * three questions would have hidden the diagram the answers are about.
 */
export const NEW_ROUTE: RouteDraft = { matchExpression: null, partner: "none", integrationId: null };

export const routeDirty = (draft: RouteDraft, saved: RouteDraft): boolean =>
  JSON.stringify(draft) !== JSON.stringify(saved);

/**
 * A filter in words. `matchSummary` says "All messages" for an empty filter, which
 * is right in a table of many types but reads as a different answer sitting inches
 * from the node that says the same thing — so the studio picks one phrase.
 */
export const conditionText = (expr: MatchGroup | null): string =>
  !expr || expr.children.length === 0 ? "Every message" : matchSummary(expr);

export function routeFace(
  draft: RouteDraft,
  saved: RouteDraft | null,
  partnerName: string | undefined,
): Omit<StageFace, "id"> {
  const all = !draft.matchExpression || draft.matchExpression.children.length === 0;
  return {
    title: conditionText(draft.matchExpression),
    detail: draft.partner === "none" ? "no partner" : (partnerName ?? "…"),
    // A filter-less route is a legitimate catch-all, so it is "none", not broken.
    state: all ? "none" : "set",
    dirty: saved !== null && routeDirty(draft, saved),
  };
}

/**
 * Who picks up a response published on the bus.
 *
 * Publishing goes through `BusService`, which maps a message type name to the one
 * information type carrying it (the column is unique) and submits a filter
 * exchange for that type. `FilterService` then evaluates **every** bus-gateway
 * route bound to that type — not just this gateway's — so the answer spans
 * gateways, and saying otherwise would under-report the fan-out.
 */
export interface BusListener {
  gatewayId: number;
  gatewayName: string;
  routeId: number;
  integrationId: number;
  integrationName: string;
  condition: string;
}

export interface BusDestination {
  informationType?: InformationTypeRow;
  listeners: BusListener[];
}

export function busDestination(
  messageTypeName: string,
  informationTypes: InformationTypeRow[],
  gateways: BusGatewayRow[],
): BusDestination {
  const informationType = informationTypes.find(
    (t) => (t.busMessageTypeName ?? "").toLowerCase() === messageTypeName.toLowerCase(),
  );
  if (!informationType) return { listeners: [] };
  const listeners = gateways
    .filter((g) => g.informationTypeId === informationType.id)
    .flatMap((g) =>
      g.routes.map((r) => ({
        gatewayId: g.id,
        gatewayName: g.name,
        routeId: r.id,
        integrationId: r.integrationId,
        integrationName: r.integrationName,
        condition: conditionText(r.matchExpression),
      })),
    );
  return { informationType, listeners };
}

/** Health of the integration behind a route, for the list and the rolled-up chain cards. */
export type RouteHealth = "ok" | "failing" | "disabled" | "paused" | "unknown";

export function routeHealth(row: IntegrationRow | undefined): RouteHealth {
  if (!row) return "unknown";
  if (!row.enabled) return "disabled";
  if (row.paused) return "paused";
  if (row.consecutiveFailures > 0) return "failing";
  return "ok";
}

export const HEALTH_LABEL: Record<RouteHealth, string> = {
  ok: "Healthy",
  failing: "Failing",
  disabled: "Disabled",
  paused: "Paused",
  unknown: "—",
};

export const HEALTH_DOT: Record<RouteHealth, string> = {
  ok: "bg-ok-600",
  failing: "bg-danger-600",
  disabled: "bg-ink-300",
  paused: "bg-warn-400",
  unknown: "bg-ink-200",
};

/** What each dot means, for hovering — `HEALTH_LABEL` alone is a single word with no context. */
export const HEALTH_TITLE: Record<RouteHealth, string> = {
  ok: "Healthy — enabled, unpaused, and its last run succeeded.",
  failing: "Enabled and unpaused, but its recent runs ended in errors.",
  disabled: "Turned off — this route's integration won't run, even if the filter matches.",
  paused: "Held without being disabled — matches still route here, but nothing runs until unpaused.",
  unknown: "No integration data available for this route yet.",
};
