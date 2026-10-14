import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useLocation, useNavigate, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { DownloadCloud, FileStack, Play, Trash2, X } from "lucide-react";
import { api } from "../../api";
import { useSessionCan } from "../../auth/useSessionCan";
import { Button, EmptyState, FormError, LoadError, LoadingBlock, ProblemText } from "../../components/ui/basics";
import { isNotFound } from "../../components/ui/isNotFound";
import { ConfirmDialog } from "../../components/ui/overlays";
import { DeleteDialog, counted } from "../../components/ui/DeleteDialog";
import { MoreActionsMenu, RunStateControl, type RunStateOption } from "../../components/layout/HeaderActions";
import { dialogsOpen } from "../../components/ui/dialogStack";
import { CodeBadge, EditableTitle, Panel, UnsavedBar } from "../../components/ui/Panel";
import { AdapterConfig } from "../../components/config/AdapterConfig";
import { useAdapterCatalog } from "../../components/config/adapterCatalog";
import { MatchExpressionEditor } from "../../components/config/MatchExpressionEditor";
import { ScheduleEditor } from "../../components/config/ScheduleEditor";
import { AggregationRetentionWarning } from "../../components/config/AggregationRetentionWarning";
import { AggregationFields } from "../../components/config/AggregationFields";
import { useSourceDocument } from "../../components/config/sourceValues";
import { SubscriptionStatusBadges, TypeBadge } from "../../components/config/shared";
import { scheduleFault } from "../../components/config/subscriptionLabels";
import { useSubscriptionsCache } from "../../components/config/lookups";
import { RevokedAdapterNotice } from "../../components/config/RevokedAdapterNotice";
import { STAGES, stagesFor, type StageId } from "./studio/stages";
import { DataSourceBinding } from "./studio/DataSourceBinding";
import { useBindsToDataSource } from "../data-sources/providers";
import { StageRail } from "./studio/StageRail";
import { faceOf } from "./studio/faces";
import { EntryPointsTable, Overview } from "./studio/Overview";
import { ResponseFields } from "./studio/ResponseFields";
import { ResponseTrigger } from "./studio/ResponseTrigger";
import { draftOf, entryPointsOf, feedersOf, stageDirty, type Draft } from "./studio/model";
import { BackLink } from "../../components/ui/BackLink";
import { keys } from "../../api/queryKeys";
import { useDetour } from "../../lib/detour";
import { notificationSlot, notificationsIncomplete, withPickedChannel } from "../../lib/notificationChannels";
import { useLeaveGuard } from "../../lib/useLeaveGuard";
import { useCurrentPage } from "../../lib/currentPage";
import { SUBSCRIPTION_PAGE, SUBSCRIPTION_PAGES, subscriptionPath } from "../../lib/subscriptionPaths";
import { PAGES, pathOf } from "../../pages";
import { usePageTitle } from "../../lib/pageTitle";

type RunState = "running" | "paused" | "off";

// Paused and Off as the server treats them: a paused subscription is still matched and its
// exchanges are kept on hold; a disabled one is neither matched nor scheduled.
const runStates: RunStateOption<RunState>[] = [
  { value: "running", label: "Running", caption: "Running: accepts work and delivers it." },
  {
    value: "paused",
    label: "Paused",
    caption: "Paused: keeps accepting work and holds every exchange until you resume.",
  },
  {
    value: "off",
    label: "Off",
    caption: "Off: not matched or scheduled. Work that arrives meanwhile is not kept.",
  },
];

/**
 * Keyed by id. Going from one subscription's page straight to another's - down a response
 * chain, say - keeps the same route mounted, and without a key the draft of the one you left
 * stayed on screen as the next one's: its name in the title, its save bar, and its settings
 * written over the next one's on Save.
 */
export function SubscriptionPage() {
  const { id = "" } = useParams();
  return <SubscriptionStudio key={id} />;
}

function SubscriptionStudio() {
  const { id = "" } = useParams();
  const subscriptionId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("subscriptions.edit");
  const canOperate = useSessionCan("subscriptions.operate");
  const canCreate = useSessionCan("subscriptions.create");
  const canDelete = useSessionCan("subscriptions.delete");
  const [params, setParams] = useSearchParams();

  const subscription = useQuery({
    queryKey: keys.subscriptions.detail(subscriptionId),
    queryFn: () => api.getSubscription(subscriptionId),
    retry: false,
  });
  usePageTitle(subscription.data?.name);
  const allSubscriptions = useSubscriptionsCache();
  // promoted properties power the legacy message filter
  const infoType = useQuery({
    queryKey: keys.informationTypes.detail(subscription.data?.informationTypeId),
    queryFn: () => api.getInformationType(subscription.data!.informationTypeId),
    enabled: subscription.data?.type === "Internal",
  });
  const sourceValues = useSourceDocument({
    type: subscription.data?.type,
    id: subscriptionId,
    informationTypeId: subscription.data?.informationTypeId,
  });

  // Loaded up front rather than per stage: the cards name their adapter, so the
  // rail needs every catalog before anything is selected.
  const receivers = useAdapterCatalog("receiver");
  const validators = useAdapterCatalog("validator");
  const mappers = useAdapterCatalog("mapper");
  const handlers = useAdapterCatalog("handler");

  // Shares the scheduled-jobs page's cache entry. The only per-stage fault we
  // can honestly attribute - it comes from the scheduler's own trigger state.
  // Which adapters are database providers, so the connection controls appear only where they mean
  // something. Called with the other queries because this component returns early below, and a
  // hook after that point runs in a different order on the two paths.
  const bindsToDataSource = useBindsToDataSource();

  const scheduleHealth = useQuery({
    queryKey: keys.subscriptions.scheduleHealth,
    queryFn: () => api.listScheduleHealth(),
    enabled: subscription.data?.type === "Receiving" || subscription.data?.type === "Aggregation",
  });

  const [draft, setDraft] = useState<Draft | null>(null);
  const [deleting, setDeleting] = useState(false);
  // The state being asked for, not a flag: the dialog then confirms and writes the
  // move the operator actually opened it for, rather than re-deriving it from a
  // `s.enabled` that a refetch could have flipped underneath the open dialog.
  const [moving, setMoving] = useState<RunState | null>(null);
  const [confirmingReceive, setConfirmingReceive] = useState(false);
  const [confirmingAggregate, setConfirmingAggregate] = useState(false);
  /** The response subscription asked to be opened while this page had unsaved changes. */
  const [opening, setOpening] = useState<number | null>(null);
  const [loaded, setLoaded] = useState(false);
  // Back from creating a response subscription: the edits as they were left, with that one
  // picked. Used for the first load only - Discard after that goes back to what is saved.
  const detour = useDetour<Draft>();
  const returning = useRef({
    kept: detour.kept,
    picked: detour.pickedResponse,
    pickedChannel: detour.pickedChannel,
    slot: detour.slot,
  });

  useEffect(() => {
    if (!loaded && subscription.data) {
      const { kept, picked, pickedChannel, slot } = returning.current;
      returning.current = { kept: null, picked: null, pickedChannel: null, slot: null };
      let next = kept ?? draftOf(subscription.data);
      if (picked !== null) next = { ...next, responseSubscriptionId: picked };
      // Back from making a channel in one of the notification rows: that row gets it.
      if (pickedChannel !== null)
        next = { ...next, notifications: withPickedChannel(next.notifications, slot, pickedChannel) };
      setDraft(next);
      setLoaded(true);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [subscription.data, loaded]);

  const dirty = useMemo(() => {
    if (!subscription.data || !draft) return false;
    return JSON.stringify(draft) !== JSON.stringify(draftOf(subscription.data));
  }, [subscription.data, draft]);

  // The mapping editor's link takes the picked mapper along, so a mapper choice alone isn't lost
  // by following it. Anything else unsaved would be.
  const dirtyBesidesMapper = useMemo(() => {
    if (!subscription.data || !draft) return false;
    const withoutMapper = (d: Draft) => ({ ...d, mapperId: null, mapperProperties: null, mapperVersion: null });
    return JSON.stringify(withoutMapper(draft)) !== JSON.stringify(withoutMapper(draftOf(subscription.data)));
  }, [subscription.data, draft]);

  // Escape closes the open stage - but only when it holds nothing unsaved. A
  // config panel that can be dismissed onto a half-finished handler is how you
  // lose an operator's work; if there are edits, Escape does nothing and the
  // save bar stays the way out.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      // A dialog on top owns Escape - see `dialogsOpen`.
      if (dialogsOpen()) return;
      const open = params.get("stage") as StageId | null;
      if (!open || !subscription.data || !draft) return;
      // let a combobox or a text field have its own Escape first
      const el = document.activeElement;
      if (el instanceof HTMLElement && ["INPUT", "TEXTAREA", "SELECT"].includes(el.tagName)) return;
      if (stageDirty(open, draft, draftOf(subscription.data))) return;
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev);
          next.delete("stage");
          return next;
        },
        { replace: true },
      );
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [params, draft, subscription.data, setParams]);

  // One call, because `all` is a prefix of `detail` and already covers it. Invalidating both
  // cancelled the detail refetch the first call had just started (invalidateQueries cancels by
  // default), so the promise awaited below belonged to the cancelled one and could resolve before
  // the fresh data landed - the very race the await exists to prevent.
  const invalidate = () => queryClient.invalidateQueries({ queryKey: keys.subscriptions.all });

  const save = useMutation({
    mutationFn: () => api.updateSubscription(subscriptionId, draft!),
    onSuccess: async () => {
      // Await the detail refetch before re-syncing the draft (avoids stale-data race).
      await invalidate();
      setLoaded(false);
    },
  });

  const pause = useMutation({
    mutationFn: () => api.pauseSubscription(subscriptionId),
    onSuccess: invalidate,
  });

  // Its own one-field write rather than a draft edit ridden along on the save
  // bar: this is the switch that drops work on the floor, so it applies when
  // asked for and not at some later save of unrelated configuration. The draft
  // is patched to match so an operator's half-finished edits below survive it -
  // and so the value doesn't immediately read back as unsaved.
  const setEnabled = useMutation({
    mutationFn: (enabled: boolean) => api.updateSubscription(subscriptionId, { enabled }),
    onSuccess: async (updated) => {
      await invalidate();
      setDraft((d) => (d ? { ...d, enabled: updated.enabled } : d));
    },
  });

  const aggregate = useMutation({
    mutationFn: () => api.aggregateNow(subscriptionId),
    onSuccess: async () => {
      await invalidate();
      void queryClient.invalidateQueries({ queryKey: keys.subscriptions.runs(subscriptionId) });
      void queryClient.invalidateQueries({ queryKey: keys.subscriptions.lastRuns });
    },
  });

  const clearRunning = useMutation({
    mutationFn: () => api.clearRunning(subscriptionId),
    onSuccess: async () => {
      await invalidate();
      void queryClient.invalidateQueries({ queryKey: keys.subscriptions.scheduleHealth });
    },
  });

  const receive = useMutation({
    mutationFn: () => api.receiveNow(subscriptionId),
    onSuccess: async () => {
      await invalidate();
      void queryClient.invalidateQueries({ queryKey: keys.subscriptions.runs(subscriptionId) });
      void queryClient.invalidateQueries({ queryKey: keys.subscriptions.lastRuns });
    },
  });

  const { leave, dialog: leaveDialog } = useLeaveGuard(dirty, {
    carriedTo: (path) => path === `/subscriptions/${subscriptionId}/mapper` && !dirtyBesidesMapper,
  });

  // Opened from a link that couldn't tell its type (or an old bookmark): move to the address of
  // its own section, so the sidebar, the breadcrumb and the way back all name the right list.
  // A replace, so Back doesn't return to the address it just left.
  const location = useLocation();
  const current = useCurrentPage();
  const type = subscription.data?.type;
  useEffect(() => {
    if (!type || current === null || !SUBSCRIPTION_PAGES.has(current)) return;
    const home = SUBSCRIPTION_PAGE[type];
    if (home !== current) navigate(subscriptionPath(subscriptionId, type) + location.search + location.hash, { replace: true });
  }, [type, current, subscriptionId, navigate, location.search, location.hash]);

  // The list this section belongs to, for the way back from a pasted link.
  const listPage = (current && PAGES[current].parent) || "subscriptions";

  if (subscription.isPending) return <LoadingBlock label="Loading subscription…" />;
  if (subscription.isError && !isNotFound(subscription.error))
    return <LoadError error={subscription.error} what="this subscription" onRetry={() => void subscription.refetch()} />;
  if (subscription.isError)
    return (
      <EmptyState title="This subscription no longer exists">
        <Link to="/subscriptions" className="font-medium link">
          Back to subscriptions
        </Link>
      </EmptyState>
    );

  const s = subscription.data;
  const set = <K extends keyof Draft>(key: K, value: Draft[K]) =>
    setDraft((d) => (d ? { ...d, [key]: value } : d));

  const isReceiver = s.type === "Receiving";
  const isInternal = s.type === "Internal";
  const isApiCall = s.type === "ApiCall";
  const isAggregation = s.type === "Aggregation";
  const isResponse = s.type === "Response";
  const aggregationSource =
    allSubscriptions.data?.find((x) => x.id === s.aggregationForId) ?? null;
  const paused = s.pausedOn !== null;
  // Off wins over Paused: a disabled subscription is not matched at all, so whether
  // it would hold work is moot until it is turned back on.
  const runState: RunState = !s.enabled ? "off" : paused ? "paused" : "running";
  // A response subscription has no gateway of its own; what feeds it is the subscriptions
  // that hand it their response.
  const entryPoints = isResponse ? feedersOf(s.id, allSubscriptions.data) : entryPointsOf(s);

  const stages = stagesFor(s.type);

  const stageParam = params.get("stage") as StageId | null;
  const stage = stageParam && stages.includes(stageParam) ? stageParam : null;
  const selectStage = (next: StageId | null) =>
    setParams(
      (prev) => {
        const p = new URLSearchParams(prev);
        if (next) p.set("stage", next);
        else p.delete("stage");
        return p;
      },
      { replace: true },
    );

  // A direct subscription with no gateway is normal: something calls it by id. Only a
  // gateway type with nothing in front of it is a real fault, so only that reads as one.
  const triggerEmpty = isApiCall ? (
    "Called directly by id, so there is nothing to configure here."
  ) : (
    <ProblemText plain>Not wired into any gateway yet, so nothing runs it.</ProblemText>
  );

  const saved = draftOf(s);
  const health = scheduleHealth.data?.find((h) => h.subscriptionId === s.id);
  const fault = scheduleFault(health) ?? undefined;

  const faces = stages.map((id) =>
    faceOf(id, {
      type: s.type,
      draft: draft ?? saved,
      saved,
      catalogs: { receivers, validators, mappers, handlers },
      entryPoints,
      nextRunOn: s.nextReceiveOn,
      // The fault belongs to the Schedule node now that aggregations have one. It used
      // to be pinned to the aggregation node too, which was the only node they had -
      // and that node could not be edited, so the page reported a broken schedule and
      // offered no way to fix it.
      fault: id === "schedule" ? fault : undefined,
      subscriptionNames: allSubscriptions.data,
      aggregationForId: s.aggregationForId,
    }),
  );

  const renderStage = (stageId: StageId) => {
    if (!draft) return null;
    const { label, description } = STAGES[stageId];
    switch (stageId) {
      case "trigger":
        return (
          <Panel
            title={label}
            description={
              isInternal ? "Which documents of this type the subscription picks up." : description
            }
          >
            {isResponse ? (
              <ResponseTrigger
                feeders={entryPoints}
                runOnBadResponses={draft.runOnBadResponses}
                onChange={(runOnBadResponses) => set("runOnBadResponses", runOnBadResponses)}
                disabled={!canEdit}
              />
            ) : isInternal ? (
              <MatchExpressionEditor
                value={draft.matchExpression}
                onChange={(matchExpression) => set("matchExpression", matchExpression)}
                properties={infoType.data?.promotedProperties ?? []}
                disabled={!canEdit}
                informationTypeId={infoType.data?.id}
                format={infoType.data?.format}
              />
            ) : (
              <EntryPointsTable rows={entryPoints} empty={triggerEmpty} />
            )}
            {/* Said once, quietly, where the trigger is edited: the history behind the type,
                not a warning about it. */}
            {(isInternal || isApiCall) && (
              <p className="mt-3 text-[12.5px] text-ink-500">
                A direct subscription. This type predates gateways and keeps working as it is; new
                subscriptions are fed through a gateway or a scheduled job instead.
              </p>
            )}
          </Panel>
        );
      case "source":
        return (
          <Panel title={label} description={description}>
            <AdapterConfig
              kind="receiver"
              adapterId={draft.receiverId}
              properties={draft.receiverProperties}
              onChange={(adapterId, properties) =>
                // A pin belongs to the adapter it was made on, so choosing another clears it.
                setDraft((d) =>
                  d
                    ? {
                        ...d,
                        receiverId: adapterId,
                        receiverProperties: properties,
                        receiverVersion: adapterId === d.receiverId ? d.receiverVersion : null,
                      }
                    : d,
                )
              }
              version={draft.receiverVersion}
              onVersionChange={(v) => set("receiverVersion", v)}
              disabled={!canEdit}
              required
            />
            {bindsToDataSource(draft.receiverId, "receiver") && (
              <div className="mt-3">
                <DataSourceBinding
                  slot="receiver"
                  siblings={[{ slot: "transformation", adapterId: draft.mapperId }, { slot: "delivery", adapterId: draft.handlerId }]}
                  dataSourceId={draft.dataSourceId}
                  properties={draft.receiverProperties}
                  onDataSourceChange={(dataSourceId) => set("dataSourceId", dataSourceId)}
                  onPropertiesChange={(receiverProperties) =>
                    setDraft((d) => (d ? { ...d, receiverProperties } : d))
                  }
                  disabled={!canEdit}
                />
              </div>
            )}
          </Panel>
        );
      case "schedule":
        return (
          <Panel
            title={label}
            description={
              isAggregation ? "When the source's exchanges are rolled up." : description
            }
          >
            <ScheduleEditor
              schedules={draft.schedules}
              onChange={(schedules) => set("schedules", schedules)}
              disabled={!canEdit}
            />
            {isAggregation && <AggregationRetentionWarning schedules={draft.schedules} />}
          </Panel>
        );
      case "aggregation":
        return (
          <Panel title={label} description={description}>
            <AggregationFields
              source={aggregationSource}
              target={draft.aggregationTarget}
              onTargetChange={(aggregationTarget) => set("aggregationTarget", aggregationTarget)}
              disabled={!canEdit}
            />
          </Panel>
        );
      case "validation":
        return (
          <Panel title={label} description={description}>
            <AdapterConfig
              kind="validator"
              adapterId={draft.validatorId}
              properties={draft.validatorProperties}
              onChange={(adapterId, properties) =>
                // A pin belongs to the adapter it was made on, so choosing another clears it.
                setDraft((d) =>
                  d
                    ? {
                        ...d,
                        validatorId: adapterId,
                        validatorProperties: properties,
                        validatorVersion: adapterId === d.validatorId ? d.validatorVersion : null,
                      }
                    : d,
                )
              }
              version={draft.validatorVersion}
              onVersionChange={(v) => set("validatorVersion", v)}
              disabled={!canEdit}
              noneLabel="None - accept every document"
            />
          </Panel>
        );
      case "transformation":
        return (
          <Panel title={label} description={description}>
            <AdapterConfig
              kind="mapper"
              adapterId={draft.mapperId}
              properties={draft.mapperProperties}
              onChange={(adapterId, properties) =>
                // A pin belongs to the adapter it was made on, so choosing another clears it.
                setDraft((d) =>
                  d
                    ? {
                        ...d,
                        mapperId: adapterId,
                        mapperProperties: properties,
                        mapperVersion: adapterId === d.mapperId ? d.mapperVersion : null,
                      }
                    : d,
                )
              }
              version={draft.mapperVersion}
              onVersionChange={(v) => set("mapperVersion", v)}
              disabled={!canEdit}
              noneLabel="None - the document passes through unchanged"
              mapperEditorHref={`/subscriptions/${s.id}/mapper`}
            />
            {bindsToDataSource(draft.mapperId, "mapper") && (
              <div className="mt-3">
                <DataSourceBinding
                  slot="mapper"
                  siblings={[{ slot: "source", adapterId: draft.receiverId }, { slot: "delivery", adapterId: draft.handlerId }]}
                  dataSourceId={draft.dataSourceId}
                  properties={draft.mapperProperties}
                  onDataSourceChange={(dataSourceId) => set("dataSourceId", dataSourceId)}
                  onPropertiesChange={(mapperProperties) =>
                    setDraft((d) => (d ? { ...d, mapperProperties } : d))
                  }
                  disabled={!canEdit}
                />
              </div>
            )}
          </Panel>
        );
      case "delivery":
        return (
          <Panel title={label} description={description}>
            <AdapterConfig
              kind="handler"
              adapterId={draft.handlerId}
              properties={draft.handlerProperties}
              onChange={(adapterId, properties) =>
                // A pin belongs to the adapter it was made on, so choosing another clears it.
                setDraft((d) =>
                  d
                    ? {
                        ...d,
                        handlerId: adapterId,
                        handlerProperties: properties,
                        handlerVersion: adapterId === d.handlerId ? d.handlerVersion : null,
                      }
                    : d,
                )
              }
              version={draft.handlerVersion}
              onVersionChange={(v) => set("handlerVersion", v)}
              disabled={!canEdit}
              noneLabel="None - the document stops here"
              sourceValues={sourceValues}
            />
            {bindsToDataSource(draft.handlerId, "handler") && (
              <div className="mt-3">
                <DataSourceBinding
                  slot="handler"
                  siblings={[{ slot: "source", adapterId: draft.receiverId }, { slot: "transformation", adapterId: draft.mapperId }]}
                  dataSourceId={draft.dataSourceId}
                  properties={draft.handlerProperties}
                  onDataSourceChange={(dataSourceId) => set("dataSourceId", dataSourceId)}
                  onPropertiesChange={(handlerProperties) =>
                    setDraft((d) => (d ? { ...d, handlerProperties } : d))
                  }
                  disabled={!canEdit}
                />
              </div>
            )}
          </Panel>
        );
      case "response":
        return (
          <Panel title={label} description={description}>
            <ResponseFields
              handlerId={draft.handlerId}
              responseSubscriptionId={draft.responseSubscriptionId}
              responseMessageTypeName={draft.responseMessageTypeName}
              onChange={(patch) => setDraft((d) => (d ? { ...d, ...patch } : d))}
              disabled={!canEdit}
              candidates={(allSubscriptions.data ?? []).filter((x) => x.id !== subscriptionId)}
              idPrefix="in-resp"
              onNewResponseSubscription={() => leave(() => detour.leave(draft, subscriptionId))}
              onOpenResponseSubscription={(target) =>
                dirty ? setOpening(target) : navigate(`/subscriptions/${target}`)
              }
            />
          </Panel>
        );
    }
  };

  return (
    <div className="pb-24">
      {leaveDialog}
      <BackLink to={pathOf(listPage, {})!} label={PAGES[listPage].title} />

      {health?.stuck && (
        <div role="alert" className="mb-4 rounded-lg border border-danger-200 bg-danger-50 px-4 py-3 text-sm text-danger-800">
          <p className="font-medium">Every scheduled run is being skipped.</p>
          <p className="mt-0.5">
            It is still marked as running from a run that didn't finish - usually one stopped with its node - so each
            run that comes due sees it busy and skips. The mark clears itself after a while; clearing it now lets the
            next run start.
          </p>
          {canOperate && (
            <div className="mt-2 flex items-center gap-3">
              <Button size="sm" busy={clearRunning.isPending} onClick={() => clearRunning.mutate()}>
                Clear the running mark
              </Button>
              {clearRunning.error && <span>{clearRunning.error.message}</span>}
            </div>
          )}
        </div>
      )}

      <div className="mb-5 flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h1 className="flex flex-wrap items-center gap-2.5 text-[22px] font-semibold tracking-tight text-ink-900">
            <EditableTitle
              value={draft?.name ?? s.name}
              onChange={(v) => set("name", v)}
              disabled={!canEdit}
              placeholder="Subscription name"
            />
            <TypeBadge type={s.type} />
            {/* Status is reported here and changed from the running-state control on the right.
                It used to be a clickable chip, indistinguishable from the Paused
                badge beside it, that only edited the draft - so the lossy switch
                saved later and quietly, while Pause, which keeps the work, asked
                first and applied at once. */}
            <SubscriptionStatusBadges enabled={s.enabled} paused={paused} />
          </h1>
          <p className="mt-1 text-sm text-ink-500">
            Carries{" "}
            <Link to={`/information-types/${s.informationTypeId}`} className="hover:underline">
              <CodeBadge code={s.informationTypeCode} name={s.informationTypeName} className="align-middle" />
            </Link>
            .
            {/* The way back up a response chain, as the Response step's Open is the way down. */}
            {isResponse &&
              (entryPoints.length > 0 ? (
                <>
                  {" "}Runs on the response of{" "}
                  {entryPoints.map((e, i) => (
                    <span key={e.key}>
                      {i > 0 && (i === entryPoints.length - 1 ? " and " : ", ")}
                      <Link to={e.href} className="font-medium text-ink-700 hover:link">
                        {e.name}
                      </Link>
                    </span>
                  ))}
                  .
                </>
              ) : (
                " Nothing hands it a response yet, so it never runs."
              ))}
          </p>
        </div>
        {/* On a phone the actions take their own row and wrap, rather than pushing the page wider
            than the screen with Delete off its edge. */}
        <div className="w-full sm:w-auto sm:shrink-0 sm:text-right">
          <div className="flex flex-wrap gap-2 sm:justify-end">
            {/* On the header rather than inside the Schedule stage: an operator
                shouldn't have to know which card hides the button. */}
            {isReceiver && canOperate && (
              <Button onClick={() => setConfirmingReceive(true)}>
                <DownloadCloud className="size-4" /> Receive now
              </Button>
            )}
            {isAggregation && canOperate && (
              <Button
                onClick={() => setConfirmingAggregate(true)}
                // AggregationJob returns immediately for a disabled subscription, so the
                // button would appear to work and record nothing at all. The list page
                // already refuses for the same reason; these two must agree.
                disabled={!s.enabled}
                title={
                  s.enabled
                    ? "Collect everything outstanding immediately and run the delivery, without waiting for the schedule."
                    : "Enable the aggregation first - a disabled one never runs, even when triggered by hand."
                }
              >
                <Play className="size-4" /> Roll up now
              </Button>
            )}
            {/* Pause and Disable as one control: the two questions an operator has
                are the same shape - stop it, or hold it - and the answer differs only
                in whether the work survives, which the caption says for the state it
                is in. Shown read-only to a session that can change neither. */}
            <RunStateControl
              label="Running state"
              options={runStates}
              value={runState}
              onChange={setMoving}
              disabled={!canOperate && !canEdit}
            />
            <MoreActionsMenu
              actions={[
                // Creating the aggregation from here rather than from a type picker: the
                // thing being rolled up is the page you are standing on, so it can never
                // be the wrong one. Not offered on an aggregation - roll-ups of roll-ups
                // are not a shape anyone has asked for, and the picker refuses them too.
                {
                  label: "Create an aggregation of this",
                  icon: <FileStack className="size-4" />,
                  title: "Create an aggregation that collects this subscription's exchanges on a schedule into one summary exchange.",
                  onSelect: () => navigate(`/aggregations/new?source=${s.id}`),
                  hidden: isAggregation || !canCreate,
                },
                {
                  label: "Delete",
                  icon: <Trash2 className="size-4" />,
                  danger: true,
                  onSelect: () => setDeleting(true),
                  hidden: !canDelete,
                },
              ]}
            />
          </div>
          <FormError>{receive.error?.message ?? aggregate.error?.message}</FormError>
        </div>
      </div>

      <RevokedAdapterNotice
        uses={(
          [
            ["receiver", s.receiverId, s.receiverVersion],
            ["validator", s.validatorId, s.validatorVersion],
            ["mapper", s.mapperId, s.mapperVersion],
            ["handler", s.handlerId, s.handlerVersion],
          ] as const
        )
          .filter(([, adapterId]) => !!adapterId)
          .map(([kind, adapterId, version]) => ({ kind, adapterId: adapterId!, version: version ?? null }))}
      />

      <StageRail faces={faces} selected={stage} onSelect={selectStage} />

      {stage === null ? (
        draft && (
          <Overview
            s={s}
            draft={draft}
            set={set}
            canEdit={canEdit}
            entryPoints={entryPoints}
            scheduled={isReceiver || isAggregation}
            onNewChannel={(index) => leave(() => detour.newChannel(draft, notificationSlot(index)))}
            onEditChannel={(channelId) => leave(() => detour.openChannel(draft, channelId))}
          />
        )
      ) : (
        // Overlaid rather than passed to each Panel as an `action`: no stage
        // uses that slot, and this keeps the eight panels free of chrome.
        <div className="relative">
          {renderStage(stage)}
          <button
            type="button"
            onClick={() => selectStage(null)}
            aria-label="Close and show the overview"
            title="Close  Esc"
            className="absolute top-2.5 right-3 rounded-md p-1.5 text-ink-500 hover:bg-ink-100 hover:text-ink-700"
          >
            <X className="size-4" />
          </button>
        </div>
      )}

      {canEdit && dirty && (
        <UnsavedBar
          busy={save.isPending}
          error={save.error?.message}
          blocked={
            draft && notificationsIncomplete(draft.notifications)
              ? "Every notification needs a channel of its own and at least one outcome ticked."
              : undefined
          }
          onSave={() => save.mutate()}
          onDiscard={() => setLoaded(false)}
        />
      )}

      {/* Within the enabled states, moving is the pause toggle alone. */}
      {moving !== null && moving !== "off" && runState !== "off" && (
        <ConfirmDialog
          title={moving === "running" ? "Resume this subscription?" : "Pause this subscription?"}
          body={
            moving === "running"
              ? `${s.name} releases its held exchanges and starts processing again.`
              : `${s.name} keeps accepting work but holds every exchange until you resume it.`
          }
          confirmLabel={moving === "running" ? "Resume" : "Pause"}
          confirmDisabled={!canOperate}
          onConfirm={async () => {
            await pause.mutateAsync();
          }}
          onClose={() => setMoving(null)}
        />
      )}

      {/* Into or out of Off is the enabled switch, with the pause toggled alongside
          when the state asked for needs it: Off to Running releases anything still
          held from before, Off to Paused holds first so nothing slips through between
          the two writes. */}
      {moving !== null && (moving === "off" || runState === "off") && (
        <ConfirmDialog
          title={moving === "off" ? "Turn this subscription off?" : moving === "paused" ? "Turn this subscription on, paused?" : "Turn this subscription on?"}
          body={
            moving === "off" ? (
              <>
                <strong className="font-medium text-ink-800">{s.name}</strong> stops being matched
                and stops being scheduled. Anything that arrives for it while it is off is{" "}
                <strong className="font-medium text-ink-800">not kept</strong> - pause it instead to
                hold that work and release it later.
              </>
            ) : moving === "paused" ? (
              `${s.name} starts being matched and scheduled again, and holds every exchange until you resume it.`
            ) : (
              `${s.name} starts being matched and scheduled again.`
            )
          }
          confirmLabel={moving === "off" ? "Turn off" : "Turn on"}
          // Paused on either side of the switch also takes the pause toggle.
          confirmDisabled={!canEdit || (moving !== "off" && paused !== (moving === "paused") && !canOperate)}
          onConfirm={async () => {
            if (moving === "off") {
              await setEnabled.mutateAsync(false);
              return;
            }
            if (moving === "paused" && !paused) await pause.mutateAsync();
            await setEnabled.mutateAsync(true);
            if (moving === "running" && paused) await pause.mutateAsync();
          }}
          onClose={() => setMoving(null)}
        />
      )}

      {confirmingReceive && (
        <ConfirmDialog
          title="Receive now?"
          body={`${s.name} checks its source immediately - anything found becomes new exchanges, outside the regular schedule.`}
          confirmLabel="Receive now"
          onConfirm={async () => {
            await receive.mutateAsync();
          }}
          onClose={() => setConfirmingReceive(false)}
        />
      )}

      {opening !== null && (
        <ConfirmDialog
          title="Save your changes first?"
          body={
            <>
              Opening{" "}
              <strong className="font-medium text-ink-800">
                {allSubscriptions.data?.find((x) => x.id === opening)?.name ?? "it"}
              </strong>{" "}
              leaves this page, so your changes to {draft?.name || s.name} are saved first. To drop them
              instead, cancel and Discard them from the bar below.
            </>
          }
          confirmLabel="Save and open"
          onConfirm={async () => {
            await save.mutateAsync();
            leave(() => navigate(`/subscriptions/${opening}`));
          }}
          onClose={() => setOpening(null)}
        />
      )}

      {confirmingAggregate && (
        <ConfirmDialog
          title="Roll up now?"
          body={`${s.name} collects everything outstanding immediately and runs its delivery - outside the regular schedule.`}
          confirmLabel="Roll up now"
          onConfirm={async () => {
            await aggregate.mutateAsync();
          }}
          onClose={() => setConfirmingAggregate(false)}
        />
      )}

      {deleting && (
        <DeleteDialog
          title="Delete this subscription?"
          name={s.name}
          // What the server refuses the delete over, as far as this page can see it. Aggregations
          // rolling it up are refused too, but the list data does not say which source each has;
          // the server's answer names them.
          blockers={[
            isResponse
              ? counted(entryPoints.length, "subscription handing it its response", "subscriptions handing it their response")
              : counted(entryPoints.length, "gateway route or attachment", "gateway routes and attachments"),
          ]}
          body={
            <>
              <strong className="font-medium text-ink-800">{s.name}</strong> and its configuration
              will be gone for good. One a gateway or another subscription still points at can't be
              deleted - it will say which.
            </>
          }
          confirmLabel="Delete subscription"
          onConfirm={async () => {
            await api.deleteSubscription(subscriptionId);
            void queryClient.invalidateQueries({ queryKey: keys.subscriptions.all });
            leave(() => navigate("/subscriptions", { replace: true }));
          }}
          onClose={() => setDeleting(false)}
        />
      )}
    </div>
  );
}
