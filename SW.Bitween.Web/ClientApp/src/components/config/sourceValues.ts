import { useQueries, useQuery } from "@tanstack/react-query";
import { api, type InformationType, type SubscriptionInfo, type SubscriptionType } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSubscriptionsCache } from "./shared";

/**
 * What a subscription can read from the original document — the input of the exchange whose
 * delivery got the response it runs on: `{{source.PATH}}` in its handler, and the "Original" value
 * kind in its mapping. A response subscription is fed by whatever hands it a response; a bus
 * gateway subscription by whatever publishes its response as the message its gateway reads.
 *
 * The paths are offered from the last document each feeder received, and their values are only
 * examples: each exchange reads its own. Any path can still be typed: documents vary, so one the
 * last document lacks is a warning, not a refusal.
 */
export interface SourceDocument {
  /** Every path the feeders' last documents have, each with the first example found and whose it is. */
  paths: { path: string; example: string; from: string }[];
  /** The feeders that have run, with the paths their last document has. */
  feeders: { id: number; name: string; xchangeId: string; paths: Set<string> }[];
  /** Whether anything feeds it yet. */
  fed: boolean;
}

/** The subscription being edited, as far as source values care. */
export interface SourceValuesFor {
  type: SubscriptionType | null | undefined;
  /** Null while it is being created. */
  id: number | null;
  /** The information type it carries, which names the message a bus gateway reads. */
  informationTypeId?: number | null;
}

/**
 * What a subscription can read from its feeders' delivered documents. Null for any type that is
 * never handed them.
 *
 * @param alsoFedBy Feeders not linked to it yet, e.g. the previous hop on the bus-gateway canvas.
 */
export function useSourceDocument(
  subscription: SourceValuesFor,
  alsoFedBy: (number | null | undefined)[] = [],
): SourceDocument | null {
  const fed = subscription.type === "Response" || subscription.type === "BusGateway";
  const subscriptions = useSubscriptionsCache();
  const types = useQuery({
    queryKey: keys.informationTypes.list,
    queryFn: () => api.listInformationTypes(),
    enabled: fed && subscription.type === "BusGateway",
  });

  const feederIds = fed
    ? [
        ...new Set([
          ...feederSubscriptions(subscription, subscriptions.data ?? [], types.data ?? []),
          ...alsoFedBy.filter((id): id is number => id != null && id > 0),
        ]),
      ]
    : [];

  // Each feeder's paths are a partner's data, so a viewer without exchanges.view gets a refusal;
  // that leaves nothing to offer, and typing a path still works.
  const received = useQueries({
    queries: feederIds.map((id) => ({
      queryKey: keys.subscriptions.sourcePaths(id),
      queryFn: () => api.getSourcePaths(id),
      retry: false,
    })),
  });
  if (!fed) return null;

  const nameOf = (id: number) => subscriptions.data?.find((s) => s.id === id)?.name ?? `#${id}`;
  return sourceDocument(
    feederIds.length > 0,
    received.flatMap((q) =>
      q.data?.xchangeId
        ? [{ id: q.data.subscriptionId, name: nameOf(q.data.subscriptionId), xchangeId: q.data.xchangeId, paths: q.data.paths }]
        : [],
    ),
  );
}

/** Puts the feeders' last documents together into what the editor offers. */
export function sourceDocument(
  fed: boolean,
  feeders: { id: number; name: string; xchangeId: string; paths: { path: string; example: string }[] }[],
): SourceDocument {
  const paths = new Map<string, { example: string; from: string }>();
  for (const feeder of feeders)
    for (const p of feeder.paths) if (!paths.has(p.path)) paths.set(p.path, { example: p.example, from: feeder.name });

  return {
    fed,
    paths: [...paths].map(([path, p]) => ({ path, ...p })),
    feeders: feeders.map((f) => ({ ...f, paths: new Set(f.paths.map((p) => p.path)) })),
  };
}

/**
 * The subscriptions feeding this one: those handing a response subscription their response, or
 * publishing theirs as the message a bus gateway reads.
 */
export function feederSubscriptions(
  subscription: SourceValuesFor,
  subscriptions: SubscriptionInfo[],
  types: InformationType[],
): number[] {
  // The bus routes on the lower-cased name, so that is what is compared.
  const message =
    subscription.type === "BusGateway"
      ? types.find((t) => t.id === subscription.informationTypeId)?.busMessageTypeName?.toLowerCase()
      : undefined;

  return subscriptions
    .filter((s) =>
      s.id === subscription.id
        ? false
        : subscription.type === "Response"
          ? subscription.id !== null && s.responseSubscriptionId === subscription.id
          : subscription.type === "BusGateway"
            ? !!message && s.responseMessageTypeName?.toLowerCase() === message
            : false,
    )
    .map((s) => s.id);
}

/** The feeders whose last document has no value at `path`. Paths match exactly. */
export const lackingPath = (source: SourceDocument, path: string) =>
  source.feeders.filter((f) => !f.paths.has(path));

/**
 * Names the feeders whose last document lacks the path, for a warning. What a missing value
 * does is the caller's to say: a handler fails the exchange, a mapping writes an empty field.
 */
export const lackingPathWarning = (lacking: SourceDocument["feeders"]) =>
  `Not in the last document ${lacking.map((f) => `"${f.name}"`).join(", ")} received`;
