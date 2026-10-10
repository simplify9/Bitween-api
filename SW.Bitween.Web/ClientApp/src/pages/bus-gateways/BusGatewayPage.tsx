import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Pause, PanelLeftClose, PanelLeftOpen, Play, Trash2 } from "lucide-react";
import { api, type BusGatewayDetail, type InlineSubscriptionDraft, type SubscriptionDetail } from "../../api";
import { Can } from "../../auth/guards";
import { useSessionCan } from "../../auth/useSessionCan";
import { Badge, Button, EmptyState, FormError, LoadError, LoadingBlock } from "../../components/ui/basics";
import { isNotFound } from "../../components/ui/isNotFound";
import { ConfirmDialog } from "../../components/ui/overlays";
import { dialogsOpen } from "../../components/ui/dialogStack";
import { CodeBadge, EditableTitle } from "../../components/ui/Panel";
import { SearchSelect } from "../../components/ui/SearchSelect";
import { useAdapterCatalog } from "../../components/config/adapterCatalog";
import { useSubscriptionRowsById, useSubscriptionsCache } from "../../components/config/lookups";
import { useSourceDocument } from "../../components/config/sourceValues";
import {
  EMPTY_SUBSCRIPTION,
  NEW_SUBSCRIPTION_ID,
  draftOf,
  isNewResponseSubscriptionId,
  newResponseSubscriptionId,
} from "../subscriptions/studio/model";
import { adapterIncomplete } from "../subscriptions/studio/faces";
import { Canvas, type Hop } from "./studio/Canvas";
import {
  DeliveryBody,
  Inspector,
  SubscriptionBody,
  ResponseBody,
  RouteBody,
  TransformationBody,
} from "./studio/Inspector";
import { PartnerDialog } from "../../components/config/PartnerDialog";
import { RouteList, type Selection } from "./studio/RouteList";
import { SourceDialog } from "./SourceDialog";
import { ConnectionBadge } from "../data-sources/ConnectionBadge";
import { BackLink } from "../../components/ui/BackLink";
import { keys } from "../../api/queryKeys";
import { useLeaveGuard } from "../../lib/useLeaveGuard";
import {
  BUS_NODES,
  NEW_ROUTE,
  OWNER,
  busDestination,
  nodeDirty,
  routeDirty,
  routeDraftOf,
  routeFace,
  type BusNodeId,
  type SubscriptionDraft,
  type RouteDraft,
} from "./studio/model";
import { usePageTitle } from "../../lib/pageTitle";

const LIST_KEY = "bitween-bus-studio-list";

/** Which record is being edited, with the snapshot the save bar compares against. */
interface RouteEdit {
  routeId: number | "new";
  draft: RouteDraft;
  /** null while the route is being added — nothing to compare to yet. */
  saved: RouteDraft | null;
}
interface SubscriptionEdit {
  subscriptionId: number;
  draft: SubscriptionDraft;
  saved: SubscriptionDraft;
  /**
   * A response subscription being defined here only: the information type of the responses it
   * runs on. Not in the draft because nothing else asks — every other hop's is fixed.
   */
  informationTypeId?: number | null;
}

/**
 * The bus gateway, as a workspace rather than a table of routes.
 *
 * A route is three answers — what it matches, whose values it runs with, and what
 * it runs — and the thing an operator actually needs to see is the fourth: what
 * happens after that. The table could show the first three and nothing else, so
 * every question past "which subscription" meant leaving the page. Here the whole
 * path is one diagram and every part of it is editable in place: the route, the
 * subscription behind it, its delivery, its response, and whoever picks that
 * response up.
 *
 * Keyed by id. Opening another gateway from here — a listener card on the canvas — keeps
 * the same route mounted, and without a key the page you left stayed in its state: its
 * name in the title, read back as an unsaved rename, which Save wrote onto the new gateway.
 */
export function BusGatewayPage() {
  const { id = "" } = useParams();
  return <BusGatewayStudio key={id} />;
}

function BusGatewayStudio() {
  const { id = "" } = useParams();
  const gatewayId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("bus-gateways.edit");
  const canEditSubscription = useSessionCan("subscriptions.edit");
  const [params, setParams] = useSearchParams();

  const gateway = useQuery({
    queryKey: keys.busGateways.detail(gatewayId),
    queryFn: () => api.getBusGateway(gatewayId),
    retry: false,
  });
  usePageTitle(gateway.data?.name);
  // One list serves three needs: the gateway's own promoted properties, its bus
  // message name, and resolving which type carries a published response.
  const informationTypes = useQuery({
    queryKey: keys.informationTypes.list,
    queryFn: () => api.listInformationTypes(),
  });
  // Every gateway, because a response on the bus wakes routes on all of them.
  const allGateways = useQuery({ queryKey: keys.busGateways.list, queryFn: () => api.listBusGateways() });
  const partners = useQuery({ queryKey: keys.partners.list, queryFn: () => api.listPartners() });
  const rowsById = useSubscriptionRowsById();
  const allSubscriptions = useSubscriptionsCache();
  const catalogs = {
    receivers: useAdapterCatalog("receiver"),
    validators: useAdapterCatalog("validator"),
    mappers: useAdapterCatalog("mapper"),
    handlers: useAdapterCatalog("handler"),
  };

  const [name, setName] = useState<string | null>(null);
  const [routeEdit, setRouteEdit] = useState<RouteEdit | null>(null);
  /**
   * One per hop that has been opened, by subscription id. Kept per hop rather than for the open
   * one alone, so defining a response subscription — which changes the hop before it too — and
   * moving between the two loses neither.
   */
  const [edits, setEdits] = useState<Record<number, SubscriptionEdit>>({});
  const [collapsedInspector, setCollapsedInspector] = useState(false);
  const [listOpen, setListOpen] = useState(() => localStorage.getItem(LIST_KEY) !== "0");
  /** undefined = closed, null = creating, number = editing that partner's values. */
  const [partnerDialog, setPartnerDialog] = useState<number | null | undefined>(undefined);
  const [removingRoute, setRemovingRoute] = useState<number | null>(null);
  const [deletingGateway, setDeletingGateway] = useState(false);
  const [confirmingActive, setConfirmingActive] = useState(false);
  const [editingSource, setEditingSource] = useState(false);
  /** A move the user asked for that would drop unsaved edits. */
  const [guarded, setGuarded] = useState<null | { what: string; go: () => void }>(null);

  const selection: Selection =
    params.get("route") === "new" ? "new" : params.get("route") ? Number(params.get("route")) : null;
  const nodeParam = params.get("node");
  const node = nodeParam && nodeParam in BUS_NODES ? (nodeParam as BusNodeId) : null;
  const activeHop = Math.min(2, Math.max(0, Number(params.get("hop") ?? 0)));

  const setQuery = (patch: Record<string, string | null>) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        for (const [k, v] of Object.entries(patch)) {
          if (v === null) next.delete(k);
          else next.set(k, v);
        }
        return next;
      },
      { replace: true },
    );

  // ——— drafts ———

  const g: BusGatewayDetail | undefined = gateway.data;

  useEffect(() => {
    if (g && name === null) setName(g.name);
  }, [g, name]);

  // Land on something: an empty canvas teaches nothing about the gateway.
  useEffect(() => {
    if (g && selection === null && g.routes.length > 0) setQuery({ route: String(g.routes[0].id) });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [g, selection]);

  useEffect(() => {
    if (selection === null) {
      if (routeEdit) setRouteEdit(null);
      return;
    }
    if (routeEdit?.routeId === selection) return;
    if (selection === "new") {
      setRouteEdit({ routeId: "new", draft: { ...NEW_ROUTE }, saved: null });
      return;
    }
    const found = g?.routes.find((r) => r.id === selection);
    if (found) setRouteEdit({ routeId: selection, draft: routeDraftOf(found), saved: routeDraftOf(found) });
  }, [selection, g, routeEdit]);

  // ——— the response chain, up to three hops ———

  const id0 = routeEdit?.draft.subscriptionId ?? null;
  const q0 = useQuery({
    queryKey: keys.subscriptions.detail(id0),
    queryFn: () => api.getSubscription(id0!),
    // A subscription being defined here has no server side to fetch yet.
    enabled: id0 !== null && id0 > 0,
  });
  const d0 = useHopDraft(edits, id0, q0.data);
  const id1 = d0?.responseSubscriptionId ?? null;
  const q1 = useQuery({
    queryKey: keys.subscriptions.detail(id1),
    queryFn: () => api.getSubscription(id1!),
    enabled: id1 !== null && id1 > 0,
  });
  const d1 = useHopDraft(edits, id1, q1.data);
  const id2 = d1?.responseSubscriptionId ?? null;
  const q2 = useQuery({
    queryKey: keys.subscriptions.detail(id2),
    queryFn: () => api.getSubscription(id2!),
    enabled: id2 !== null && id2 > 0,
  });
  const d2 = useHopDraft(edits, id2, q2.data);

  const chain = [
    { id: id0, draft: d0, data: q0.data },
    { id: id1, draft: d1, data: q1.data },
    { id: id2, draft: d2, data: q2.data },
  ].filter((h): h is { id: number; draft: SubscriptionDraft | null; data: typeof q0.data } => h.id !== null);

  const activeIndex = Math.min(activeHop, Math.max(0, chain.length - 1));
  const active = chain[activeIndex];
  const activeData = active?.data;
  const edit = active ? (edits[active.id] ?? null) : null;

  // What a hop's handler can read from the delivered document. The first hop runs on the route,
  // fed by whatever publishes its response as the gateway's message. A response hop is fed by the
  // hop before it whether or not that link is saved yet; one not saved itself has delivered nothing
  // to offer paths from.
  const previousHop = activeIndex > 0 ? chain[activeIndex - 1] : null;
  const activeType =
    activeData?.type ??
    (active && isNewResponseSubscriptionId(active.id)
      ? "Response"
      : activeIndex === 0
        ? "BusGateway"
        : undefined);
  const hopSourceValues = useSourceDocument(
    {
      type: activeType,
      id: active && active.id > 0 ? active.id : null,
      informationTypeId:
        activeData?.informationTypeId ?? (activeIndex === 0 ? g?.informationTypeId : null),
    },
    activeType === "Response" ? [previousHop?.id] : [],
  );
  const setEdit = (change: (e: SubscriptionEdit) => SubscriptionEdit) =>
    setEdits((all) => (active && all[active.id] ? { ...all, [active.id]: change(all[active.id]) } : all));

  // Seed the open hop's edit the first time it is opened. Keyed on the subscription id, so a
  // draft can never be applied to the wrong subscription.
  useEffect(() => {
    if (!active?.id || edits[active.id]) return;
    if (active.id === NEW_SUBSCRIPTION_ID) {
      // Blank, and `saved` blank too: every field the user fills counts as a change,
      // so the save bar names them the same way it does for an existing subscription.
      setEdits((all) => ({
        ...all,
        [NEW_SUBSCRIPTION_ID]: {
          subscriptionId: NEW_SUBSCRIPTION_ID,
          draft: structuredClone(EMPTY_SUBSCRIPTION),
          saved: structuredClone(EMPTY_SUBSCRIPTION),
        },
      }));
      return;
    }
    // A new response subscription is seeded when it is started — see `startNewResponse`.
    if (active.id < 0 || !activeData || activeData.id !== active.id) return;
    const seeded = draftOf(activeData);
    setEdits((all) => ({ ...all, [active.id]: { subscriptionId: active.id, draft: seeded, saved: structuredClone(seeded) } }));
  }, [active?.id, activeData, edits]);

  /**
   * Response subscriptions being defined here. Listed wherever a subscription is named, so the
   * hop feeding one says what it feeds rather than an id nothing on the server has.
   */
  const pendingResponses = chain
    .filter((h) => isNewResponseSubscriptionId(h.id))
    .map((h) => ({
      id: h.id,
      name: edits[h.id]?.draft.name.trim() || "New response subscription",
      type: "Response" as const,
    }));

  // ——— what's unsaved ———

  const nameDirty = !!g && name !== null && name !== g.name;
  const isNewRoute = routeEdit?.routeId === "new";
  const routeIsDirty = !!routeEdit && routeEdit.saved !== null && routeDirty(routeEdit.draft, routeEdit.saved);
  // Only the hops still in the chain count: a new response subscription dropped by pointing the
  // hop before it somewhere else is not saved, and says nothing about unsaved work.
  const chainEdits = chain.map((h) => edits[h.id]).filter((e): e is SubscriptionEdit => !!e);
  const editDirty = (e: SubscriptionEdit) =>
    isNewResponseSubscriptionId(e.subscriptionId) || JSON.stringify(e.draft) !== JSON.stringify(e.saved);
  const intIsDirty = chainEdits.some(editDirty);
  const dirty = nameDirty || routeIsDirty || intIsDirty || isNewRoute;

  // Named down to the field. This bar is the last thing between an edit and a
  // change in how live traffic is routed, so it says what a save will write
  // rather than just that something is unsaved.
  const routeChanges =
    routeEdit?.saved === null || !routeEdit
      ? []
      : ([
          JSON.stringify(routeEdit.draft.matchExpression) !==
            JSON.stringify(routeEdit.saved.matchExpression) && "filter",
          routeEdit.draft.partner !== routeEdit.saved.partner && "partner",
          routeEdit.draft.subscriptionId !== routeEdit.saved.subscriptionId && "subscription",
        ].filter((x): x is string => typeof x === "string"));
  const subscriptionChanges = (e: SubscriptionEdit) =>
    (Object.keys(BUS_NODES) as BusNodeId[])
      .filter((n) => OWNER[n] === "subscription" && nodeDirty(n, e.draft, e.saved))
      .map((n) => BUS_NODES[n].label.toLowerCase());
  const dirtyLabels = [
    nameDirty && "the gateway name",
    isNewRoute ? "a new route" : routeIsDirty && `the route (${routeChanges.join(", ")})`,
    ...chainEdits
      .filter(editDirty)
      .map((e) =>
        isNewResponseSubscriptionId(e.subscriptionId)
          ? `a new response subscription${e.draft.name.trim() ? ` (${e.draft.name.trim()})` : ""}`
          : `${e.draft.name || "the subscription"} (${subscriptionChanges(e).join(", ")})`,
      ),
  ].filter((x): x is string => typeof x === "string");

  const discard = () => {
    setName(g?.name ?? null);
    setRouteEdit(null);
    setEdits({});
    if (isNewRoute) setQuery({ route: g?.routes[0] ? String(g.routes[0].id) : null, node: null, hop: null });
  };

  /** Anything that would leave unsaved edits behind asks first. */
  const guard = (what: string, go: () => void) => (dirty ? setGuarded({ what, go }) : go());

  const select = (next: Selection) =>
    guard("this route", () => {
      setRouteEdit(null);
      setEdits({});
      setQuery({ route: next === null ? null : String(next), hop: null });
    });

  // No guard: every hop keeps its own edits, so moving between them loses nothing.
  const selectHop = (index: number) => setQuery({ hop: String(index) });

  /**
   * Hop `index` in the shape the API takes, with a response subscription being defined after it
   * nested inside — the two are written in one save, so neither can exist without the other.
   */
  const inlineHop = (index: number): InlineSubscriptionDraft => {
    const own = edits[chain[index].id]!;
    const next = chain[index + 1];
    const nested = next && isNewResponseSubscriptionId(next.id) ? edits[next.id] : undefined;
    return {
      ...own.draft,
      responseSubscriptionId: nested ? null : own.draft.responseSubscriptionId,
      newResponseSubscription: nested
        ? {
            ...inlineHop(index + 1),
            informationTypeId: nested.informationTypeId!,
            runOnBadResponses: nested.draft.runOnBadResponses,
          }
        : null,
    };
  };

  // Escape closes the open node, but only when it holds nothing unsaved — the
  // same rule the subscription studio applies to its stages.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape" || !node) return;
      // A dialog on top owns Escape — see `dialogsOpen`.
      if (dialogsOpen()) return;
      const el = document.activeElement;
      if (el instanceof HTMLElement && ["INPUT", "TEXTAREA", "SELECT"].includes(el.tagName)) return;
      if (OWNER[node] === "route" && (routeIsDirty || isNewRoute)) return;
      if (OWNER[node] === "subscription" && edit && nodeDirty(node, edit.draft, edit.saved)) return;
      setQuery({ node: null });
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [node, routeIsDirty, isNewRoute, edit]);

  // ——— saving ———

  const save = useMutation({
    mutationFn: async () => {
      // `inactive` is round-tripped, not edited here: Update replaces the record, so
      // omitting it would reactivate a deactivated gateway on a rename.
      if (nameDirty && name !== null)
        await api.updateBusGateway(gatewayId, { name, inactive: g.inactive });
      // A subscription being defined here is not written on its own: it goes with the
      // route — or, for a response subscription, with the hop feeding it — in the one call
      // the endpoint commits as a single transaction, so a failure can't leave a
      // subscription nothing points at.
      const definingSubscription = routeEdit?.draft.subscriptionId === NEW_SUBSCRIPTION_ID;

      // Otherwise the subscriptions first: if the route write then fails, what was saved
      // is the part that stands on its own.
      for (let i = 0; i < chain.length; i++) {
        const own = edits[chain[i].id];
        if (!own || chain[i].id < 0 || !editDirty(own)) continue;
        await api.updateSubscription(chain[i].id, inlineHop(i));
      }

      if (routeEdit && (isNewRoute || routeIsDirty)) {
        const partnerId = routeEdit.draft.partner === "none" ? null : routeEdit.draft.partner;
        if (isNewRoute) {
          const before = new Set((g?.routes ?? []).map((r) => r.id));
          await api.addBusRoute(gatewayId, {
            ...(definingSubscription
              ? { newSubscription: inlineHop(0) }
              : { subscriptionId: routeEdit.draft.subscriptionId! }),
            partnerId,
            matchExpression: routeEdit.draft.matchExpression,
          });
          return before;
        }
        await api.updateBusRoute(gatewayId, routeEdit.routeId as number, {
          subscriptionId: routeEdit.draft.subscriptionId!,
          partnerId,
          matchExpression: routeEdit.draft.matchExpression,
        });
      }
      return null;
    },
    onSuccess: async (before) => {
      // Both awaited before the drafts are dropped: re-seeding from stale data
      // would leave the save bar up over changes that are already saved.
      const fresh = await queryClient.fetchQuery({
        queryKey: keys.busGateways.detail(gatewayId),
        queryFn: () => api.getBusGateway(gatewayId),
        // This has to be the saved gateway, not the one we already had: fetchQuery honours
        // staleTime, and this key inherits the five minutes registered for bus gateways, so
        // without this it returns the copy from before the save — and the new route would be
        // missing from fresh.routes below.
        staleTime: 0,
      });
      void queryClient.invalidateQueries({ queryKey: keys.busGateways.all });
      // Awaited before the drafts are dropped: re-seeding from stale data would leave the save bar
      // up over changes that are already saved. Covers the edited route's own subscription too.
      await queryClient.invalidateQueries({ queryKey: keys.subscriptions.all });
      setRouteEdit(null);
      setEdits({});
      setName(fresh.name);
      if (before) {
        const created = fresh.routes.find((r) => !before.has(r.id));
        setQuery({ route: created ? String(created.id) : null, hop: null });
      }
    },
  });

  const { leave, dialog: leaveDialog } = useLeaveGuard(dirty);

  if (gateway.isPending) return <LoadingBlock label="Loading bus gateway…" />;
  if (gateway.isError && !isNotFound(gateway.error))
    return <LoadError error={gateway.error} what="this bus gateway" onRetry={() => void gateway.refetch()} />;
  if (gateway.isError || !g)
    return (
      <EmptyState title="This bus gateway no longer exists">
        <Link to="/bus-gateways" className="font-medium text-crimson-700 hover:underline">
          Back to bus gateways
        </Link>
      </EmptyState>
    );

  const ownType = informationTypes.data?.find((t) => t.id === g.informationTypeId);
  const partnerName =
    routeEdit && typeof routeEdit.draft.partner === "number"
      ? partners.data?.find((p) => p.id === routeEdit.draft.partner)?.name
      : undefined;

  const hops: Hop[] = chain.map((h) => ({
    subscriptionId: h.id,
    name:
      h.draft?.name?.trim() ||
      h.data?.name ||
      (h.id === NEW_SUBSCRIPTION_ID
        ? "New subscription"
        : isNewResponseSubscriptionId(h.id)
          ? "New response subscription"
          : `#${h.id}`),
    draft: h.draft,
    saved: edits[h.id]?.saved ?? h.draft,
    row: rowsById.get(h.id),
    destination:
      h.draft?.responseMessageTypeName && informationTypes.data
        ? busDestination(h.draft.responseMessageTypeName, informationTypes.data, allGateways.data ?? [])
        : null,
  }));

  // What still blocks a save, said the way the modal used to say it at its Create
  // button. The rule outlives the modal: a subscription defined here cannot be saved
  // half-made, and the server refuses it too.
  const defining = edits[NEW_SUBSCRIPTION_ID];
  const missing = [
    ...(routeEdit?.draft.subscriptionId === NEW_SUBSCRIPTION_ID && defining
      ? [
          defining.draft.name.trim().length < 2 && "a name",
          !defining.draft.handlerId && "a delivery",
          adapterIncomplete(catalogs.handlers, defining.draft.handlerId, defining.draft.handlerProperties) &&
            "its required delivery fields",
        ]
      : isNewRoute && routeEdit?.draft.subscriptionId === null
        ? ["a subscription"]
        : []),
    // The same for a response subscription being defined, which also has to be told what it carries.
    ...chainEdits
      .filter((e) => isNewResponseSubscriptionId(e.subscriptionId))
      .flatMap((e) => [
        e.draft.name.trim().length < 2 && "a name for the new response subscription",
        (e.informationTypeId ?? null) === null && "the information type it carries",
        !e.draft.handlerId && "its delivery",
        adapterIncomplete(catalogs.handlers, e.draft.handlerId, e.draft.handlerProperties) &&
          "its required delivery fields",
      ]),
  ].filter((m): m is string => typeof m === "string");

  const nodeIsDirty = node
    ? OWNER[node] === "route"
      ? routeIsDirty || isNewRoute
      : OWNER[node] === "subscription" && !!edit && nodeDirty(node, edit.draft, edit.saved)
    : false;

  /**
   * Which of the route's subscription's adapter slots reference a partner value. Read off
   * the draft rather than the saved record, so picking "No partner" and pasting a
   * `{{partner.…}}` URL in the same sitting is still caught before the save.
   *
   * `d0`, not `edit.draft`: the partner belongs to the route, so the slots that read it
   * are the first hop's. `edit` follows whichever hop is open, and a response hop's
   * properties would answer for a subscription the partner was never set on.
   */
  const partnerTokenSlots = (
    [
      ["receiver", d0?.receiverProperties],
      ["mapper", d0?.mapperProperties],
      ["handler", d0?.handlerProperties],
    ] as const
  )
    .filter(([, props]) => Object.values(props ?? {}).some((v) => v.includes("{{partner.")))
    .map(([slot]) => slot);

  const renderNode = () => {
    if (!node) return null;
    if (node === "route")
      return (
        routeEdit && (
          <RouteBody
            draft={routeEdit.draft}
            onChange={(patch) => setRouteEdit((r) => (r ? { ...r, draft: { ...r.draft, ...patch } } : r))}
            promotedProperties={ownType?.promotedProperties ?? []}
            informationTypeId={g.informationTypeId}
            informationTypeCode={g.informationTypeCode}
            informationTypeFormat={ownType?.format}
            disabled={!canEdit}
            partnerTokenSlots={partnerTokenSlots}
            onNewPartner={() => setPartnerDialog(null)}
            onEditPartner={(id) => setPartnerDialog(id)}
            // Only a new route: updating one takes an existing subscription, never a new one.
            onNewSubscription={
              isNewRoute
                ? () => {
                    // No modal: the route draft points at the subscription being defined, and the
                    // canvas draws it like any other. Straight to its own node, where the name is.
                    setRouteEdit((r) =>
                      r ? { ...r, draft: { ...r.draft, subscriptionId: NEW_SUBSCRIPTION_ID } } : r,
                    );
                    setQuery({ node: "subscription" });
                  }
                : undefined
            }
          />
        )
      );
    if (!edit)
      return (
        <p className="text-sm text-ink-500">
          {routeEdit?.draft.subscriptionId === null
            ? "Pick the subscription this route runs, or define one — this step belongs to it."
            : "Loading the subscription…"}
        </p>
      );
    const onChange = (patch: Partial<SubscriptionDraft>) =>
      setEdit((e) => ({ ...e, draft: { ...e.draft, ...patch } }));
    const isNewHop = edit.subscriptionId < 0;
    const isResponseHop = isNewResponseSubscriptionId(edit.subscriptionId) || activeData?.type === "Response";

    // Its cards drawn straight on the canvas as the next hop, the way a route's new subscription
    // is. Not past the third hop — the canvas draws no further, so it could not be edited.
    const startNewResponse =
      activeIndex < 2
        ? () => {
            const id = newResponseSubscriptionId(activeIndex + 1);
            const at = active!.id;
            setEdits((all) => ({
              ...all,
              [at]: { ...all[at], draft: { ...all[at].draft, responseSubscriptionId: id } },
              [id]: {
                subscriptionId: id,
                draft: structuredClone(EMPTY_SUBSCRIPTION),
                saved: structuredClone(EMPTY_SUBSCRIPTION),
                informationTypeId: null,
              },
            }));
            setQuery({ hop: String(activeIndex + 1), node: "subscription" });
          }
        : undefined;
    switch (node) {
      case "subscription":
        return (
          <SubscriptionBody
            draft={edit.draft}
            onChange={onChange}
            disabled={!canEditSubscription}
            health={
              activeData
                ? { isRunning: activeData.isRunning, consecutiveFailures: activeData.consecutiveFailures }
                : null
            }
            lastException={activeData?.lastException ?? null}
            autoFocusName={isNewHop}
            response={
              isResponseHop
                ? {
                    informationType: isNewResponseSubscriptionId(edit.subscriptionId)
                      ? {
                          value: edit.informationTypeId ?? null,
                          onChange: (informationTypeId) => setEdit((e) => ({ ...e, informationTypeId })),
                        }
                      : undefined,
                  }
                : undefined
            }
          />
        );
      case "transformation":
        return (
          <TransformationBody
            draft={edit.draft}
            onChange={onChange}
            disabled={!canEditSubscription}
            mapperEditorHref={isNewHop ? null : `/subscriptions/${edit.subscriptionId}/mapper`}
          />
        );
      case "delivery":
        return (
          <DeliveryBody
            draft={edit.draft}
            onChange={onChange}
            disabled={!canEditSubscription}
            sourceValues={hopSourceValues}
          />
        );
      case "response":
        return (
          <ResponseBody
            draft={edit.draft}
            onChange={onChange}
            disabled={!canEditSubscription}
            candidates={[...(allSubscriptions.data ?? []), ...pendingResponses].filter(
              (x) => x.id !== edit.subscriptionId,
            )}
            onNewResponseSubscription={startNewResponse}
            // It is already the next hop on the canvas, so opening it is selecting that hop —
            // unless this is the last hop the canvas draws, and then it is its own page.
            onOpenResponseSubscription={(target) => {
              const at = chain.findIndex((h) => h.id === target);
              if (at > activeIndex) setQuery({ hop: String(at), node: "subscription" });
              else guard("this route", () => leave(() => navigate(`/subscriptions/${target}`)));
            }}
          />
        );
    }
  };

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      {leaveDialog}
      {editingSource && <SourceDialog gateway={g} onClose={() => setEditingSource(false)} />}

      {/* ——— toolbar ——— */}
      <div className="flex shrink-0 flex-wrap items-center gap-x-4 gap-y-2 border-b border-ink-200 bg-white px-4 py-2.5">
        <BackLink to="/bus-gateways" label="Bus gateways" className="shrink-0" />
        <span className="h-5 w-px shrink-0 bg-ink-200" aria-hidden />
        <button
          type="button"
          onClick={() => {
            localStorage.setItem(LIST_KEY, listOpen ? "0" : "1");
            setListOpen(!listOpen);
          }}
          title={listOpen ? "Hide the route list and give the canvas the screen" : "Show the route list"}
          className="hidden shrink-0 items-center gap-1.5 rounded-lg px-2 py-1 text-[13px] font-medium text-ink-600 hover:bg-ink-100 lg:inline-flex"
        >
          {listOpen ? <PanelLeftClose className="size-4" /> : <PanelLeftOpen className="size-4" />}
          {g.routes.length} route{g.routes.length === 1 ? "" : "s"}
        </button>

        <h1 className="min-w-0 shrink-0 text-[17px] font-semibold tracking-tight text-ink-900">
          <EditableTitle
            value={name ?? g.name}
            onChange={setName}
            disabled={!canEdit}
            placeholder="Gateway name"
          />
        </h1>
        {g.inactive && <Badge tone="warn">Deactivated</Badge>}
        {/* The information type, its bus message name, and whether it is even on the
            bus. This was a canvas node, but it is a property of the gateway, not of
            any one route — repeating it on every route's diagram said otherwise. */}
        <Link
          to={`/information-types/${g.informationTypeId}`}
          className="flex shrink-0 items-center gap-1.5"
          title={
            g.dataSourceId == null
              ? `${g.informationTypeName} — listens on the bus${
                  ownType?.busMessageTypeName ? ` as ${ownType.busMessageTypeName}` : ""
                }`
              : `${g.informationTypeName} — arrives from ${g.dataSourceName}`
          }
        >
          <CodeBadge code={g.informationTypeCode} name={g.informationTypeName} />
          {/* The bus message name, and the warning when there isn't one, apply only to a gateway
              on the INTERNAL bus. An external one is fed by its data source's adapter and never
              touches Bitween's own bus, so "not on the bus" would be a fault it does not have. */}
          {g.dataSourceId == null &&
            ownType &&
            (ownType.busMessageTypeName ? (
              <code className="font-mono text-[11px] text-ink-500">{ownType.busMessageTypeName}</code>
            ) : (
              <span className="rounded-md bg-danger-100 px-1.5 py-0.5 text-[11px] font-medium text-danger-800">
                Not on the bus
              </span>
            ))}
        </Link>

        {/* Where the messages come from. Next to the information type because the two are one
            thought — what arrives, and from where — and both belong to the gateway rather than to
            any one route. */}
        <button
          type="button"
          onClick={() => canEdit && setEditingSource(true)}
          disabled={!canEdit}
          title={
            g.dataSourceId == null
              ? "Reads Bitween's own internal bus. Click to read a broker outside Bitween instead."
              : `Reads ${g.endpoint ?? "—"} on ${g.dataSourceName}. Click to change.`
          }
          className="flex shrink-0 items-center gap-1.5 rounded-lg px-2 py-1 text-[13px] text-ink-600 enabled:hover:bg-ink-100"
        >
          {g.dataSourceId == null ? (
            <span className="text-ink-500">Internal bus</span>
          ) : (
            <>
              <span className="font-medium text-ink-800">{g.dataSourceName}</span>
              <code className="font-mono text-[11px] text-ink-500">{g.endpoint}</code>
              <ConnectionBadge state={g.dataSourceState} />
            </>
          )}
        </button>

        {/* With the list hidden there still has to be a way to reach route 94 of
            127, and scrolling isn't it. */}
        {!listOpen && (
          <div className="w-72 shrink-0">
            <SearchSelect
              size="sm"
              aria-label="Jump to a route"
              value={typeof selection === "number" ? String(selection) : ""}
              placeholder="Jump to a route…"
              onChange={(v) => v !== "" && select(Number(v))}
              options={g.routes.map((r) => ({
                value: String(r.id),
                label: r.subscriptionName || `#${r.subscriptionId}`,
                code: r.partnerName ?? undefined,
                hint: r.partnerName ?? "Any partner",
              }))}
            />
          </div>
        )}

        <span className="flex-1" />
        {canEdit && typeof selection === "number" && (
          <Button size="sm" onClick={() => setRemovingRoute(selection)}>
            <Trash2 className="size-3.5" /> Remove route
          </Button>
        )}
        {canEdit && (
          <Button
            size="sm"
            onClick={() => setConfirmingActive(true)}
            title={
              g.inactive
                ? "Start offering this gateway's messages to its routes again."
                : "Stop messages reaching its routes, without deleting them."
            }
          >
            {g.inactive ? <Play className="size-3.5" /> : <Pause className="size-3.5" />}
            {g.inactive ? "Activate" : "Deactivate"}
          </Button>
        )}
        <Can permission="bus-gateways.delete">
          <Button size="sm" variant="danger" onClick={() => setDeletingGateway(true)}>
            <Trash2 className="size-3.5" /> Delete gateway
          </Button>
        </Can>
      </div>

      {/* ——— workspace ——— */}
      <div className="flex min-h-0 flex-1 flex-col">
        <div className="relative flex min-h-0 flex-1 flex-col">
          {/* Over the canvas rather than beside it: the panel is as tall as its
              routes, and the path reserves a gutter so nothing hides under it. */}
          {listOpen && (
            <div className="pointer-events-none absolute inset-y-0 left-0 z-10 hidden p-3 lg:block">
              <RouteList
                routes={g.routes}
                rowsById={rowsById}
                selected={selection}
                onSelect={select}
                onAdd={() => guard("this route", () => setQuery({ route: "new", node: "route", hop: null }))}
                pending={isNewRoute ? (routeEdit?.draft ?? null) : null}
                dirtyRouteId={routeIsDirty && typeof routeEdit?.routeId === "number" ? routeEdit.routeId : null}
                canEdit={canEdit}
              />
            </div>
          )}
          {routeEdit === null ? (
            <div className="flex min-h-0 flex-1 items-center justify-center bg-canvas p-8">
              <EmptyState title={`No routes — every ${g.informationTypeCode} message here is ignored`}>
                {canEdit && (
                  <Button
                    variant="primary"
                    onClick={() => setQuery({ route: "new", node: "route", hop: null })}
                  >
                    Add the first route
                  </Button>
                )}
              </EmptyState>
            </div>
          ) : (
            <Canvas
              routeFace={routeFace(routeEdit.draft, routeEdit.saved, partnerName)}
              informationTypeId={g.informationTypeId}
              hops={hops}
              activeHop={activeIndex}
              onSelectHop={selectHop}
              selectedNode={node}
              onSelectNode={(next) => setQuery({ node: next })}
              catalogs={catalogs}
              subscriptionNames={[...(allSubscriptions.data ?? []), ...pendingResponses]}
              onOpenListener={(l) =>
                l.gatewayId === gatewayId
                  ? select(l.routeId)
                  : guard("this route", () => leave(() => navigate(`/bus-gateways/${l.gatewayId}?route=${l.routeId}`)))
              }
              routeChosen={routeEdit.draft.subscriptionId !== null}
              gutterRem={listOpen ? 21 : 0}
              resetKey={`${selection}`}
            />
          )}

          <Inspector
            node={node}
            dirty={nodeIsDirty}
            collapsed={collapsedInspector}
            onToggleCollapsed={() => setCollapsedInspector((c) => !c)}
            onClose={() => setQuery({ node: null })}
          >
            {renderNode()}
          </Inspector>

          {dirty && (canEdit || canEditSubscription) && (
            <div className="flex shrink-0 items-center justify-between gap-3 border-t border-ink-200 bg-ink-50 px-4 py-2.5">
              <div className="min-w-0">
                <p className="truncate text-[13px] font-medium text-ink-800">
                  Unsaved: {dirtyLabels.join(", ")}
                </p>
                {missing.length > 0 ? (
                  <p className="truncate text-[13px] text-ink-500">
                    Still needs {missing.slice(0, -1).join(", ")}
                    {missing.length > 1 ? " and " : ""}
                    {missing.at(-1)}.
                  </p>
                ) : (
                  <FormError>{save.error?.message}</FormError>
                )}
              </div>
              <div className="flex shrink-0 gap-2">
                <Button size="sm" onClick={discard}>
                  {isNewRoute ? "Cancel" : "Discard"}
                </Button>
                <Button
                  size="sm"
                  variant="primary"
                  busy={save.isPending}
                  disabled={missing.length > 0}
                  onClick={() => save.mutate()}
                >
                  {isNewRoute ? "Create route" : "Save changes"}
                </Button>
              </div>
            </div>
          )}
        </div>
      </div>

      {/* ——— dialogs ——— */}
      {partnerDialog !== undefined && (
        <PartnerDialog
          partnerId={partnerDialog}
          onClose={() => setPartnerDialog(undefined)}
          onSaved={(partnerId) =>
            setRouteEdit((r) => (r ? { ...r, draft: { ...r.draft, partner: partnerId } } : r))
          }
        />
      )}

      {guarded && (
        <ConfirmDialog
          title="Discard unsaved changes?"
          body={
            <>
              You have unsaved changes to {dirtyLabels.join(", ")}. Leaving {guarded.what} now throws them
              away — Save changes first if you want to keep them.
            </>
          }
          confirmLabel="Discard and continue"
          onConfirm={async () => {
            const go = guarded.go;
            setName(g.name);
            setRouteEdit(null);
            setEdits({});
            go();
          }}
          onClose={() => setGuarded(null)}
        />
      )}

      {removingRoute !== null && (
        <ConfirmDialog
          title="Remove this route?"
          body={
            <>
              Messages matching it stop reaching{" "}
              <strong className="font-medium text-ink-800">
                {g.routes.find((r) => r.id === removingRoute)?.subscriptionName}
              </strong>
              . The subscription itself is kept.
            </>
          }
          confirmLabel="Remove route"
          onConfirm={async () => {
            await api.removeBusRoute(gatewayId, removingRoute);
            const fresh = await queryClient.fetchQuery({
              queryKey: keys.busGateways.detail(gatewayId),
              queryFn: () => api.getBusGateway(gatewayId),
              // As above: the cached copy still lists the route that was just removed, and
              // fresh.routes[0] below would select it.
              staleTime: 0,
            });
            void queryClient.invalidateQueries({ queryKey: keys.busGateways.all });
            void queryClient.invalidateQueries({ queryKey: keys.subscriptions.all });
            setRouteEdit(null);
            setEdits({});
            setQuery({ route: fresh.routes[0] ? String(fresh.routes[0].id) : null, hop: null });
          }}
          onClose={() => setRemovingRoute(null)}
        />
      )}

      {confirmingActive && (
        <ConfirmDialog
          title={g.inactive ? `Activate ${g.name}?` : `Deactivate ${g.name}?`}
          body={
            g.inactive
              ? `${g.informationTypeName} messages reach its ${g.routes.length} route${g.routes.length === 1 ? "" : "s"} again. Anything published while it was off is gone — the message was offered and this gateway wasn't listening.`
              : `${g.informationTypeName} messages stop reaching its ${g.routes.length} route${g.routes.length === 1 ? "" : "s"}. Other gateways bound to the same message are unaffected, and the routes themselves are kept.`
          }
          confirmLabel={g.inactive ? "Activate" : "Deactivate"}
          onConfirm={async () => {
            await api.updateBusGateway(gatewayId, { name: g.name, inactive: !g.inactive });
            await queryClient.invalidateQueries({ queryKey: keys.busGateways.all });
          }}
          onClose={() => setConfirmingActive(false)}
        />
      )}

      {deletingGateway && (
        <ConfirmDialog
          title="Delete this bus gateway?"
          body={
            <>
              <strong className="font-medium text-ink-800">{g.name}</strong> stops listening and all its
              routes are removed. The subscriptions behind them are kept.
            </>
          }
          confirmLabel="Delete gateway"
          onConfirm={async () => {
            await api.deleteBusGateway(gatewayId);
            void queryClient.invalidateQueries({ queryKey: keys.busGateways.all });
            void queryClient.invalidateQueries({ queryKey: keys.subscriptions.all });
            leave(() => navigate("/bus-gateways", { replace: true }));
          }}
          onClose={() => setDeletingGateway(false)}
        />
      )}
    </div>
  );
}

/**
 * A hop's current shape: its draft once it has been opened for editing, the saved
 * record otherwise. Taking the draft is what makes picking a response
 * target grow the chain on the canvas before anything is saved.
 *
 * Matched on the subscription's own id rather than on which hop is active — a
 * stale `?hop=` in the URL would otherwise leave the edited node reading from
 * saved data while the draft went nowhere.
 */
function useHopDraft(
  edits: Record<number, SubscriptionEdit>,
  subscriptionId: number | null,
  data: SubscriptionDetail | undefined,
): SubscriptionDraft | null {
  return useMemo(() => {
    if (subscriptionId === null) return null;
    if (edits[subscriptionId]) return edits[subscriptionId].draft;
    return data && data.id === subscriptionId ? draftOf(data) : null;
  }, [edits, subscriptionId, data]);
}
