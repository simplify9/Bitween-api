import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { PanelLeftClose, PanelLeftOpen, Trash2 } from "lucide-react";
import { api, type BusGatewayDetail, type IntegrationDetail } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { Button, EmptyState, FormError, LoadingBlock } from "../../components/ui/basics";
import { ConfirmDialog, dialogsOpen } from "../../components/ui/overlays";
import { CodeBadge, EditableTitle } from "../../components/ui/Panel";
import { SearchSelect } from "../../components/ui/SearchSelect";
import { useAdapterCatalog } from "../../components/config/AdapterConfig";
import { useIntegrationRowsById, useIntegrationsCache } from "../../components/config/shared";
import { draftOf } from "../integrations/studio/model";
import { Canvas, type Hop } from "./studio/Canvas";
import {
  DeliveryBody,
  Inspector,
  IntegrationBody,
  ResponseBody,
  RouteBody,
  TransformationBody,
} from "./studio/Inspector";
import { PartnerDialog } from "../../components/config/PartnerDialog";
import { NewIntegrationDialog } from "./studio/QuickCreate";
import { RouteList, type Selection } from "./studio/RouteList";
import { BackLink } from "../../components/ui/BackLink";
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
  type IntegrationDraft,
  type RouteDraft,
} from "./studio/model";

const LIST_KEY = "bitween-bus-studio-list";

/** Which record is being edited, with the snapshot the save bar compares against. */
interface RouteEdit {
  routeId: number | "new";
  draft: RouteDraft;
  /** null while the route is being added — nothing to compare to yet. */
  saved: RouteDraft | null;
}
interface IntegrationEdit {
  integrationId: number;
  draft: IntegrationDraft;
  saved: IntegrationDraft;
}

/**
 * The bus gateway, as a workspace rather than a table of routes.
 *
 * A route is three answers — what it matches, whose values it runs with, and what
 * it runs — and the thing an operator actually needs to see is the fourth: what
 * happens after that. The table could show the first three and nothing else, so
 * every question past "which integration" meant leaving the page. Here the whole
 * path is one diagram and every part of it is editable in place: the route, the
 * integration behind it, its delivery, its response, and whoever picks that
 * response up.
 */
export function BusGatewayPage() {
  const { id = "" } = useParams();
  const gatewayId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("bus-gateways.edit");
  const canEditIntegration = useSessionCan("subscriptions.edit");
  const canCreateIntegration = useSessionCan("subscriptions.create");
  const [params, setParams] = useSearchParams();

  const gateway = useQuery({
    queryKey: ["bus-gateway", gatewayId],
    queryFn: () => api.getBusGateway(gatewayId),
    retry: false,
  });
  // One list serves three needs: the gateway's own promoted properties, its bus
  // message name, and resolving which type carries a published response.
  const informationTypes = useQuery({
    queryKey: ["information-types"],
    queryFn: () => api.listInformationTypes(),
  });
  // Every gateway, because a response on the bus wakes routes on all of them.
  const allGateways = useQuery({ queryKey: ["bus-gateways"], queryFn: () => api.listBusGateways() });
  const partners = useQuery({ queryKey: ["partners"], queryFn: () => api.listPartners() });
  const rowsById = useIntegrationRowsById();
  const allIntegrations = useIntegrationsCache();
  const catalogs = {
    receivers: useAdapterCatalog("receiver"),
    validators: useAdapterCatalog("validator"),
    mappers: useAdapterCatalog("mapper"),
    handlers: useAdapterCatalog("handler"),
  };

  const [name, setName] = useState<string | null>(null);
  const [routeEdit, setRouteEdit] = useState<RouteEdit | null>(null);
  const [edit, setEdit] = useState<IntegrationEdit | null>(null);
  const [collapsedInspector, setCollapsedInspector] = useState(false);
  const [listOpen, setListOpen] = useState(() => localStorage.getItem(LIST_KEY) !== "0");
  const [creating, setCreating] = useState<null | "route-integration" | "response-integration">(null);
  /** undefined = closed, null = creating, number = editing that partner's values. */
  const [partnerDialog, setPartnerDialog] = useState<number | null | undefined>(undefined);
  const [removingRoute, setRemovingRoute] = useState<number | null>(null);
  const [deletingGateway, setDeletingGateway] = useState(false);
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

  const id0 = routeEdit?.draft.integrationId ?? null;
  const q0 = useQuery({
    queryKey: ["integration", id0],
    queryFn: () => api.getIntegration(id0!),
    enabled: id0 !== null,
  });
  const d0 = useHopDraft(edit, id0, q0.data);
  const id1 = d0?.responseIntegrationId ?? null;
  const q1 = useQuery({
    queryKey: ["integration", id1],
    queryFn: () => api.getIntegration(id1!),
    enabled: id1 !== null,
  });
  const d1 = useHopDraft(edit, id1, q1.data);
  const id2 = d1?.responseIntegrationId ?? null;
  const q2 = useQuery({
    queryKey: ["integration", id2],
    queryFn: () => api.getIntegration(id2!),
    enabled: id2 !== null,
  });
  const d2 = useHopDraft(edit, id2, q2.data);

  const chain = [
    { id: id0, draft: d0, data: q0.data },
    { id: id1, draft: d1, data: q1.data },
    { id: id2, draft: d2, data: q2.data },
  ].filter((h): h is { id: number; draft: IntegrationDraft | null; data: typeof q0.data } => h.id !== null);

  const activeIndex = Math.min(activeHop, Math.max(0, chain.length - 1));
  const active = chain[activeIndex];
  const activeData = active?.data;

  // Seed the editable hop. Keyed on the integration id, so switching hop or route
  // re-seeds and a draft can never be applied to the wrong integration.
  useEffect(() => {
    if (!active?.id) {
      if (edit) setEdit(null);
      return;
    }
    if (edit?.integrationId === active.id) return;
    if (!activeData || activeData.id !== active.id) return;
    const seeded = draftOf(activeData);
    setEdit({ integrationId: active.id, draft: seeded, saved: structuredClone(seeded) });
  }, [active?.id, activeData, edit]);

  // ——— what's unsaved ———

  const nameDirty = !!g && name !== null && name !== g.name;
  const isNewRoute = routeEdit?.routeId === "new";
  const routeIsDirty = !!routeEdit && routeEdit.saved !== null && routeDirty(routeEdit.draft, routeEdit.saved);
  const intIsDirty = !!edit && JSON.stringify(edit.draft) !== JSON.stringify(edit.saved);
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
          routeEdit.draft.integrationId !== routeEdit.saved.integrationId && "integration",
        ].filter((x): x is string => typeof x === "string"));
  const integrationChanges = edit
    ? (Object.keys(BUS_NODES) as BusNodeId[])
        .filter((n) => OWNER[n] === "integration" && nodeDirty(n, edit.draft, edit.saved))
        .map((n) => BUS_NODES[n].label.toLowerCase())
    : [];
  const dirtyLabels = [
    nameDirty && "the gateway name",
    isNewRoute ? "a new route" : routeIsDirty && `the route (${routeChanges.join(", ")})`,
    intIsDirty && `${edit ? edit.draft.name : "the integration"} (${integrationChanges.join(", ")})`,
  ].filter((x): x is string => typeof x === "string");

  const discard = () => {
    setName(g?.name ?? null);
    setRouteEdit(null);
    setEdit(null);
    if (isNewRoute) setQuery({ route: g?.routes[0] ? String(g.routes[0].id) : null, node: null, hop: null });
  };

  /** Anything that would leave unsaved edits behind asks first. */
  const guard = (what: string, go: () => void) => (dirty ? setGuarded({ what, go }) : go());

  const select = (next: Selection) =>
    guard("this route", () => {
      setRouteEdit(null);
      setEdit(null);
      setQuery({ route: next === null ? null : String(next), hop: null });
    });

  const selectHop = (index: number) =>
    guard("this integration", () => {
      setEdit(null);
      setQuery({ hop: String(index) });
    });

  // Escape closes the open node, but only when it holds nothing unsaved — the
  // same rule the integration studio applies to its stages.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape" || !node) return;
      // A dialog on top owns Escape — see `dialogsOpen`.
      if (dialogsOpen()) return;
      const el = document.activeElement;
      if (el instanceof HTMLElement && ["INPUT", "TEXTAREA", "SELECT"].includes(el.tagName)) return;
      if (OWNER[node] === "route" && (routeIsDirty || isNewRoute)) return;
      if (OWNER[node] === "integration" && edit && nodeDirty(node, edit.draft, edit.saved)) return;
      setQuery({ node: null });
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [node, routeIsDirty, isNewRoute, edit]);

  // ——— saving ———

  const save = useMutation({
    mutationFn: async () => {
      if (nameDirty && name !== null) await api.updateBusGateway(gatewayId, { name });
      // The integration first: if the route write then fails, what was saved is
      // the part that stands on its own.
      if (edit && intIsDirty) await api.updateIntegration(edit.integrationId, edit.draft);
      if (routeEdit && (isNewRoute || routeIsDirty)) {
        const input = {
          integrationId: routeEdit.draft.integrationId!,
          partnerId: routeEdit.draft.partner === "none" ? null : routeEdit.draft.partner,
          matchExpression: routeEdit.draft.matchExpression,
        };
        if (isNewRoute) {
          const before = new Set((g?.routes ?? []).map((r) => r.id));
          await api.addBusRoute(gatewayId, input);
          return before;
        }
        await api.updateBusRoute(gatewayId, routeEdit.routeId as number, input);
      }
      return null;
    },
    onSuccess: async (before) => {
      // Both awaited before the drafts are dropped: re-seeding from stale data
      // would leave the save bar up over changes that are already saved.
      const fresh = await queryClient.fetchQuery({
        queryKey: ["bus-gateway", gatewayId],
        queryFn: () => api.getBusGateway(gatewayId),
      });
      if (edit) await queryClient.invalidateQueries({ queryKey: ["integration", edit.integrationId] });
      void queryClient.invalidateQueries({ queryKey: ["bus-gateways"] });
      void queryClient.invalidateQueries({ queryKey: ["integrations"] });
      void queryClient.invalidateQueries({ queryKey: ["integration-rows"] });
      setRouteEdit(null);
      setEdit(null);
      setName(fresh.name);
      if (before) {
        const created = fresh.routes.find((r) => !before.has(r.id));
        setQuery({ route: created ? String(created.id) : null, hop: null });
      }
    },
  });

  if (gateway.isPending) return <LoadingBlock label="Loading bus gateway…" />;
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
    integrationId: h.id,
    name: h.draft?.name ?? h.data?.name ?? `#${h.id}`,
    draft: h.draft,
    saved: edit?.integrationId === h.id ? edit.saved : h.draft,
    row: rowsById.get(h.id),
    destination:
      h.draft?.responseMessageTypeName && informationTypes.data
        ? busDestination(h.draft.responseMessageTypeName, informationTypes.data, allGateways.data ?? [])
        : null,
  }));

  const nodeIsDirty = node
    ? OWNER[node] === "route"
      ? routeIsDirty || isNewRoute
      : OWNER[node] === "integration" && !!edit && nodeDirty(node, edit.draft, edit.saved)
    : false;

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
            disabled={!canEdit}
            onNewPartner={() => setPartnerDialog(null)}
            onEditPartner={(id) => setPartnerDialog(id)}
            onNewIntegration={() => setCreating("route-integration")}
          />
        )
      );
    if (!edit)
      return (
        <p className="text-sm text-ink-500">
          {routeEdit?.draft.integrationId === null
            ? "Pick the integration this route runs first — this step belongs to it."
            : "Loading the integration…"}
        </p>
      );
    const onChange = (patch: Partial<IntegrationDraft>) =>
      setEdit((e) => (e ? { ...e, draft: { ...e.draft, ...patch } } : e));
    switch (node) {
      case "integration":
        return (
          <IntegrationBody
            draft={edit.draft}
            onChange={onChange}
            disabled={!canEditIntegration}
            health={
              activeData
                ? { isRunning: activeData.isRunning, consecutiveFailures: activeData.consecutiveFailures }
                : null
            }
            lastException={activeData?.lastException ?? null}
          />
        );
      case "transformation":
        return (
          <TransformationBody
            draft={edit.draft}
            onChange={onChange}
            disabled={!canEditIntegration}
            mapperEditorHref={`/subscriptions/${edit.integrationId}/mapper`}
          />
        );
      case "delivery":
        return <DeliveryBody draft={edit.draft} onChange={onChange} disabled={!canEditIntegration} />;
      case "response":
        return (
          <ResponseBody
            draft={edit.draft}
            onChange={onChange}
            disabled={!canEditIntegration}
            candidates={(allIntegrations.data ?? []).filter((x) => x.id !== edit.integrationId)}
            onNewIntegration={() => setCreating("response-integration")}
            canCreate={canCreateIntegration}
          />
        );
    }
  };

  return (
    <div className="flex min-h-0 flex-1 flex-col">
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
        {/* The information type, its bus message name, and whether it is even on the
            bus. This was a canvas node, but it is a property of the gateway, not of
            any one route — repeating it on every route's diagram said otherwise. */}
        <Link
          to={`/information-types/${g.informationTypeId}`}
          className="flex shrink-0 items-center gap-1.5"
          title={`${g.informationTypeName} — listens on the bus${
            ownType?.busMessageTypeName ? ` as ${ownType.busMessageTypeName}` : ""
          }`}
        >
          <CodeBadge code={g.informationTypeCode} name={g.informationTypeName} />
          {ownType &&
            (ownType.busMessageTypeName ? (
              <code className="font-mono text-[11px] text-ink-400">{ownType.busMessageTypeName}</code>
            ) : (
              <span className="rounded-md bg-danger-100 px-1.5 py-0.5 text-[11px] font-medium text-danger-800">
                Not on the bus
              </span>
            ))}
        </Link>

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
                label: r.integrationName || `#${r.integrationId}`,
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
              integrationNames={allIntegrations.data ?? []}
              onOpenListener={(l) =>
                l.gatewayId === gatewayId
                  ? select(l.routeId)
                  : guard("this route", () => navigate(`/bus-gateways/${l.gatewayId}?route=${l.routeId}`))
              }
              routeChosen={routeEdit.draft.integrationId !== null}
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

          {dirty && (canEdit || canEditIntegration) && (
            <div className="flex shrink-0 items-center justify-between gap-3 border-t border-ink-200 bg-ink-50 px-4 py-2.5">
              <div className="min-w-0">
                <p className="truncate text-[13px] font-medium text-ink-800">
                  Unsaved: {dirtyLabels.join(", ")}
                </p>
                <FormError>{save.error?.message}</FormError>
              </div>
              <div className="flex shrink-0 gap-2">
                <Button size="sm" onClick={discard}>
                  {isNewRoute ? "Cancel" : "Discard"}
                </Button>
                <Button
                  size="sm"
                  variant="primary"
                  busy={save.isPending}
                  disabled={isNewRoute && routeEdit?.draft.integrationId === null}
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
      {(creating === "route-integration" || creating === "response-integration") && (
        <NewIntegrationDialog
          informationTypeId={g.informationTypeId}
          informationTypeCode={g.informationTypeCode}
          onClose={() => setCreating(null)}
          onCreated={(newId) => {
            if (creating === "route-integration")
              setRouteEdit((r) => (r ? { ...r, draft: { ...r.draft, integrationId: newId } } : r));
            else setEdit((e) => (e ? { ...e, draft: { ...e.draft, responseIntegrationId: newId } } : e));
          }}
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
            setEdit(null);
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
                {g.routes.find((r) => r.id === removingRoute)?.integrationName}
              </strong>
              . The integration itself is kept.
            </>
          }
          confirmLabel="Remove route"
          onConfirm={async () => {
            await api.removeBusRoute(gatewayId, removingRoute);
            const fresh = await queryClient.fetchQuery({
              queryKey: ["bus-gateway", gatewayId],
              queryFn: () => api.getBusGateway(gatewayId),
            });
            void queryClient.invalidateQueries({ queryKey: ["bus-gateways"] });
            void queryClient.invalidateQueries({ queryKey: ["integrations"] });
            setRouteEdit(null);
            setEdit(null);
            setQuery({ route: fresh.routes[0] ? String(fresh.routes[0].id) : null, hop: null });
          }}
          onClose={() => setRemovingRoute(null)}
        />
      )}

      {deletingGateway && (
        <ConfirmDialog
          title="Delete this bus gateway?"
          body={
            <>
              <strong className="font-medium text-ink-800">{g.name}</strong> stops listening and all its
              routes are removed. The integrations behind them are kept.
            </>
          }
          confirmLabel="Delete gateway"
          onConfirm={async () => {
            await api.deleteBusGateway(gatewayId);
            void queryClient.invalidateQueries({ queryKey: ["bus-gateways"] });
            void queryClient.invalidateQueries({ queryKey: ["integrations"] });
            navigate("/bus-gateways");
          }}
          onClose={() => setDeletingGateway(false)}
        />
      )}
    </div>
  );
}

/**
 * A hop's current shape: the live draft when this is the hop being edited, the
 * saved record otherwise. Taking the draft is what makes picking a response
 * target grow the chain on the canvas before anything is saved.
 *
 * Matched on the integration's own id rather than on which hop is active — a
 * stale `?hop=` in the URL would otherwise leave the edited node reading from
 * saved data while the draft went nowhere.
 */
function useHopDraft(
  edit: IntegrationEdit | null,
  integrationId: number | null,
  data: IntegrationDetail | undefined,
): IntegrationDraft | null {
  return useMemo(() => {
    if (integrationId === null) return null;
    if (edit?.integrationId === integrationId) return edit.draft;
    return data && data.id === integrationId ? draftOf(data) : null;
  }, [edit, integrationId, data]);
}
