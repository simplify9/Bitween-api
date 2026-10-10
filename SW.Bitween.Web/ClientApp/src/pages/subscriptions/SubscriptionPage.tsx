import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { DownloadCloud, FileStack, Pause, Play, Power, PowerOff, Trash2, X } from "lucide-react";
import { api } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { Button, EmptyState, FormError, isNotFound, LoadError, LoadingBlock } from "../../components/ui/basics";
import { ConfirmDialog, dialogsOpen } from "../../components/ui/overlays";
import { CodeBadge, EditableTitle, Panel, UnsavedBar } from "../../components/ui/Panel";
import { AdapterConfig, useAdapterCatalog } from "../../components/config/AdapterConfig";
import { MatchExpressionEditor } from "../../components/config/MatchExpressionEditor";
import { ScheduleEditor } from "../../components/config/ScheduleEditor";
import { AggregationRetentionWarning } from "../../components/config/AggregationRetentionWarning";
import { AggregationFields } from "../../components/config/AggregationFields";
import { useSourceDocument } from "../../components/config/sourceValues";
import {
  SubscriptionStatusBadges,
  TypeBadge,
  scheduleFault,
  useSubscriptionsCache,
} from "../../components/config/shared";
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
import { useResponseDetour } from "../../lib/responseDetour";

/**
 * Keyed by id. Going from one subscription's page straight to another's — down a response
 * chain, say — keeps the same route mounted, and without a key the draft of the one you left
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
  const [params, setParams] = useSearchParams();

  const subscription = useQuery({
    queryKey: keys.subscriptions.detail(subscriptionId),
    queryFn: () => api.getSubscription(subscriptionId),
    retry: false,
  });
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
  // can honestly attribute — it comes from the scheduler's own trigger state.
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
  const [confirmingPause, setConfirmingPause] = useState(false);
  // The state being asked for, not a flag: the dialog then confirms and writes the
  // action the operator actually opened it for, rather than re-deriving it from a
  // `s.enabled` that a refetch could have flipped underneath the open dialog.
  const [enabling, setEnabling] = useState<boolean | null>(null);
  const [confirmingReceive, setConfirmingReceive] = useState(false);
  const [confirmingAggregate, setConfirmingAggregate] = useState(false);
  /** The response subscription asked to be opened while this page had unsaved changes. */
  const [opening, setOpening] = useState<number | null>(null);
  const [loaded, setLoaded] = useState(false);
  // Back from creating a response subscription: the edits as they were left, with that one
  // picked. Used for the first load only — Discard after that goes back to what is saved.
  const detour = useResponseDetour<Draft>();
  const returning = useRef({ kept: detour.kept, picked: detour.pickedResponse });

  useEffect(() => {
    if (!loaded && subscription.data) {
      const { kept, picked } = returning.current;
      returning.current = { kept: null, picked: null };
      const base = kept ?? draftOf(subscription.data);
      setDraft(picked !== null ? { ...base, responseSubscriptionId: picked } : base);
      setLoaded(true);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [subscription.data, loaded]);

  const dirty = useMemo(() => {
    if (!subscription.data || !draft) return false;
    return JSON.stringify(draft) !== JSON.stringify(draftOf(subscription.data));
  }, [subscription.data, draft]);

  // Escape closes the open stage — but only when it holds nothing unsaved. A
  // config panel that can be dismissed onto a half-finished handler is how you
  // lose an operator's work; if there are edits, Escape does nothing and the
  // save bar stays the way out.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      // A dialog on top owns Escape — see `dialogsOpen`.
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
  // the fresh data landed — the very race the await exists to prevent.
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
  // is patched to match so an operator's half-finished edits below survive it —
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

  const receive = useMutation({
    mutationFn: () => api.receiveNow(subscriptionId),
    onSuccess: async () => {
      await invalidate();
      void queryClient.invalidateQueries({ queryKey: keys.subscriptions.runs(subscriptionId) });
      void queryClient.invalidateQueries({ queryKey: keys.subscriptions.lastRuns });
    },
  });

  if (subscription.isPending) return <LoadingBlock label="Loading subscription…" />;
  if (subscription.isError && !isNotFound(subscription.error))
    return <LoadError error={subscription.error} what="this subscription" onRetry={() => void subscription.refetch()} />;
  if (subscription.isError)
    return (
      <EmptyState title="This subscription no longer exists">
        <Link to="/subscriptions" className="font-medium text-crimson-700 hover:underline">
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

  const triggerEmpty = isApiCall
    ? "Invoked directly by id (legacy) — nothing to configure here."
    : "Not wired into any gateway yet — it never runs.";

  const saved = draftOf(s);
  const fault = scheduleFault(scheduleHealth.data?.find((h) => h.subscriptionId === s.id)) ?? undefined;

  const faces = stages.map((id) =>
    faceOf(id, {
      type: s.type,
      draft: draft ?? saved,
      saved,
      catalogs: { receivers, validators, mappers, handlers },
      entryPoints,
      nextRunOn: s.nextReceiveOn,
      // The fault belongs to the Schedule node now that aggregations have one. It used
      // to be pinned to the aggregation node too, which was the only node they had —
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
              noneLabel="None — accept every document"
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
              noneLabel="None — the document passes through unchanged"
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
              noneLabel="None — the document stops here"
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
              onNewResponseSubscription={() => detour.leave(draft, subscriptionId)}
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
      <BackLink to="/subscriptions" label="Subscriptions" />

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
            {/* Status is reported here and changed from the buttons on the right.
                It used to be a clickable chip, indistinguishable from the Paused
                badge beside it, that only edited the draft — so the lossy switch
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
                      <Link to={e.href} className="font-medium text-ink-700 hover:text-crimson-700 hover:underline">
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
        <div className="shrink-0 text-right">
          <div className="flex gap-2">
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
                    : "Enable the aggregation first — a disabled one never runs, even when triggered by hand."
                }
              >
                <Play className="size-4" /> Roll up now
              </Button>
            )}
            {/* Creating the aggregation from here rather than from a type picker: the
                thing being rolled up is the page you are standing on, so it can never
                be the wrong one. Not offered on an aggregation — roll-ups of roll-ups
                are not a shape anyone has asked for, and the picker refuses them too. */}
            {!isAggregation && (
              <Can permission="subscriptions.create">
                <Button
                  onClick={() => navigate(`/aggregations/new?source=${s.id}`)}
                  title="Create an aggregation that collects this subscription's exchanges on a schedule into one summary exchange."
                >
                  <FileStack className="size-4" /> Roll these up
                </Button>
              </Can>
            )}
            {canOperate && (
              <Button
                onClick={() => setConfirmingPause(true)}
                title={paused ? "Release held work and resume." : "Keep accepting work but hold it for later release."}
              >
                {paused ? <Play className="size-4" /> : <Pause className="size-4" />}
                {paused ? "Resume" : "Pause"}
              </Button>
            )}
            {/* Beside Pause, because the two questions an operator actually has
                are the same shape — stop it, or hold it — and the answer differs
                only in whether the work survives. */}
            {canEdit && (
              <Button
                onClick={() => setEnabling(!s.enabled)}
                title={
                  s.enabled
                    ? "Stop it running at all. Work that arrives while it is off is not kept — pause instead to hold it."
                    : "Let it match and run again."
                }
              >
                {s.enabled ? <PowerOff className="size-4" /> : <Power className="size-4" />}
                {s.enabled ? "Disable" : "Enable"}
              </Button>
            )}
            <Can permission="subscriptions.delete">
              <Button variant="danger" onClick={() => setDeleting(true)}>
                <Trash2 className="size-4" /> Delete
              </Button>
            </Can>
          </div>
          <FormError>{receive.error?.message ?? aggregate.error?.message}</FormError>
        </div>
      </div>

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
            className="absolute top-2.5 right-3 rounded-md p-1.5 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
          >
            <X className="size-4" />
          </button>
        </div>
      )}

      {canEdit && dirty && (
        <UnsavedBar
          busy={save.isPending}
          error={save.error?.message}
          onSave={() => save.mutate()}
          onDiscard={() => setLoaded(false)}
        />
      )}

      {confirmingPause && (
        <ConfirmDialog
          title={paused ? "Resume this subscription?" : "Pause this subscription?"}
          body={
            paused
              ? `${s.name} releases its held exchanges and starts processing again.`
              : `${s.name} keeps accepting work but holds every exchange until you resume it.`
          }
          confirmLabel={paused ? "Resume" : "Pause"}
          onConfirm={async () => {
            await pause.mutateAsync();
          }}
          onClose={() => setConfirmingPause(false)}
        />
      )}

      {enabling !== null && (
        <ConfirmDialog
          title={enabling ? "Enable this subscription?" : "Disable this subscription?"}
          body={
            enabling ? (
              `${s.name} starts being matched and scheduled again.`
            ) : (
              <>
                <strong className="font-medium text-ink-800">{s.name}</strong> stops being matched
                and stops being scheduled. Anything that arrives for it while it is off is{" "}
                <strong className="font-medium text-ink-800">not kept</strong> — pause it instead to
                hold that work and release it later.
              </>
            )
          }
          confirmLabel={enabling ? "Enable" : "Disable"}
          onConfirm={async () => {
            await setEnabled.mutateAsync(enabling);
          }}
          onClose={() => setEnabling(null)}
        />
      )}

      {confirmingReceive && (
        <ConfirmDialog
          title="Receive now?"
          body={`${s.name} checks its source immediately — anything found becomes new exchanges, outside the regular schedule.`}
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
            navigate(`/subscriptions/${opening}`);
          }}
          onClose={() => setOpening(null)}
        />
      )}

      {confirmingAggregate && (
        <ConfirmDialog
          title="Roll up now?"
          body={`${s.name} collects everything outstanding immediately and runs its delivery — outside the regular schedule.`}
          confirmLabel="Roll up now"
          onConfirm={async () => {
            await aggregate.mutateAsync();
          }}
          onClose={() => setConfirmingAggregate(false)}
        />
      )}

      {deleting && (
        <ConfirmDialog
          title="Delete this subscription?"
          body={
            <>
              <strong className="font-medium text-ink-800">{s.name}</strong> and its configuration
              will be gone for good. One a gateway or another subscription still points at can't be
              deleted — it will say which.
            </>
          }
          confirmLabel="Delete subscription"
          onConfirm={async () => {
            await api.deleteSubscription(subscriptionId);
            void queryClient.invalidateQueries({ queryKey: keys.subscriptions.all });
            navigate("/subscriptions", { replace: true });
          }}
          onClose={() => setDeleting(false)}
        />
      )}
    </div>
  );
}
