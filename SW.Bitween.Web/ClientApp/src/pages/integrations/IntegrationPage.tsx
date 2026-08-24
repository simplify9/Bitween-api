import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { DownloadCloud, Pause, Play, Trash2, X } from "lucide-react";
import { api } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { Badge, Button, EmptyState, FormError, LoadingBlock } from "../../components/ui/basics";
import { ConfirmDialog, dialogsOpen } from "../../components/ui/overlays";
import { CodeBadge, EditableTitle, Panel, UnsavedBar } from "../../components/ui/Panel";
import { AdapterConfig, useAdapterCatalog } from "../../components/config/AdapterConfig";
import { MatchExpressionEditor } from "../../components/config/MatchExpressionEditor";
import { ScheduleEditor } from "../../components/config/ScheduleEditor";
import { TypeBadge, scheduleFault, useIntegrationsCache } from "../../components/config/shared";
import { STAGES, stagesFor, type StageId } from "./studio/stages";
import { StageRail } from "./studio/StageRail";
import { faceOf } from "./studio/faces";
import { EntryPointsTable, Overview } from "./studio/Overview";
import { ResponseFields } from "./studio/ResponseFields";
import { draftOf, entryPointsOf, stageDirty, type Draft } from "./studio/model";
import { BackLink } from "../../components/ui/BackLink";

export function IntegrationPage() {
  const { id = "" } = useParams();
  const integrationId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("subscriptions.edit");
  const canOperate = useSessionCan("subscriptions.operate");
  const canCreateWorkGroup = useSessionCan("workgroups.create");
  const [params, setParams] = useSearchParams();

  const integration = useQuery({
    queryKey: ["integration", integrationId],
    queryFn: () => api.getIntegration(integrationId),
    retry: false,
  });
  const allIntegrations = useIntegrationsCache();
  // promoted properties power the legacy message filter
  const infoType = useQuery({
    queryKey: ["information-type", integration.data?.informationTypeId],
    queryFn: () => api.getInformationType(integration.data!.informationTypeId),
    enabled: integration.data?.type === "Internal",
  });

  // Loaded up front rather than per stage: the cards name their adapter, so the
  // rail needs every catalog before anything is selected.
  const receivers = useAdapterCatalog("receiver");
  const validators = useAdapterCatalog("validator");
  const mappers = useAdapterCatalog("mapper");
  const handlers = useAdapterCatalog("handler");

  // Shares the scheduled-jobs page's cache entry. The only per-stage fault we
  // can honestly attribute — it comes from the scheduler's own trigger state.
  const scheduleHealth = useQuery({
    queryKey: ["schedule-health"],
    queryFn: () => api.listScheduleHealth(),
    enabled: integration.data?.type === "Receiving" || integration.data?.type === "Aggregation",
  });

  const [draft, setDraft] = useState<Draft | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [confirmingPause, setConfirmingPause] = useState(false);
  const [confirmingReceive, setConfirmingReceive] = useState(false);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    if (!loaded && integration.data) {
      setDraft(draftOf(integration.data));
      setLoaded(true);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [integration.data, loaded]);

  const dirty = useMemo(() => {
    if (!integration.data || !draft) return false;
    return JSON.stringify(draft) !== JSON.stringify(draftOf(integration.data));
  }, [integration.data, draft]);

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
      if (!open || !integration.data || !draft) return;
      // let a combobox or a text field have its own Escape first
      const el = document.activeElement;
      if (el instanceof HTMLElement && ["INPUT", "TEXTAREA", "SELECT"].includes(el.tagName)) return;
      if (stageDirty(open, draft, draftOf(integration.data))) return;
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
  }, [params, draft, integration.data, setParams]);

  const invalidate = () => {
    const detail = queryClient.invalidateQueries({ queryKey: ["integration", integrationId] });
    void queryClient.invalidateQueries({ queryKey: ["integration-rows"] });
      void queryClient.invalidateQueries({ queryKey: ["integration-rows-search"] });
    void queryClient.invalidateQueries({ queryKey: ["integrations"] });
    return detail;
  };

  const save = useMutation({
    mutationFn: () => api.updateIntegration(integrationId, draft!),
    onSuccess: async () => {
      // Await the detail refetch before re-syncing the draft (avoids stale-data race).
      await invalidate();
      setLoaded(false);
    },
  });

  const pause = useMutation({
    mutationFn: () => api.pauseIntegration(integrationId),
    onSuccess: invalidate,
  });

  const receive = useMutation({
    mutationFn: () => api.receiveNow(integrationId),
    onSuccess: async () => {
      await invalidate();
      void queryClient.invalidateQueries({ queryKey: ["integration-runs", integrationId] });
      void queryClient.invalidateQueries({ queryKey: ["last-runs"] });
    },
  });

  if (integration.isPending) return <LoadingBlock label="Loading integration…" />;
  if (integration.isError)
    return (
      <EmptyState title="This integration no longer exists">
        <Link to="/subscriptions" className="font-medium text-crimson-700 hover:underline">
          Back to integrations
        </Link>
      </EmptyState>
    );

  const s = integration.data;
  const set = <K extends keyof Draft>(key: K, value: Draft[K]) =>
    setDraft((d) => (d ? { ...d, [key]: value } : d));

  const isReceiver = s.type === "Receiving";
  const isInternal = s.type === "Internal";
  const isApiCall = s.type === "ApiCall";
  const isAggregation = s.type === "Aggregation";
  const paused = s.pausedOn !== null;
  const entryPoints = entryPointsOf(s);

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
  const fault = scheduleFault(scheduleHealth.data?.find((h) => h.integrationId === s.id)) ?? undefined;

  const faces = stages.map((id) =>
    faceOf(id, {
      type: s.type,
      draft: draft ?? saved,
      saved,
      catalogs: { receivers, validators, mappers, handlers },
      entryPoints,
      nextRunOn: s.nextReceiveOn,
      fault: id === "schedule" || id === "aggregation" ? fault : undefined,
      integrationNames: allIntegrations.data,
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
              isInternal ? "Which documents of this type the integration picks up." : description
            }
          >
            {isInternal ? (
              <MatchExpressionEditor
                value={draft.matchExpression}
                onChange={(matchExpression) => set("matchExpression", matchExpression)}
                properties={infoType.data?.promotedProperties ?? []}
                disabled={!canEdit}
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
                setDraft((d) => (d ? { ...d, receiverId: adapterId, receiverProperties: properties } : d))
              }
              disabled={!canEdit}
              required
            />
          </Panel>
        );
      case "schedule":
        return (
          <Panel title={label} description={description}>
            <ScheduleEditor
              schedules={draft.schedules}
              onChange={(schedules) => set("schedules", schedules)}
              disabled={!canEdit}
            />
          </Panel>
        );
      case "aggregation":
        return (
          <Panel title={label} description={description}>
            <p className="text-sm text-ink-500">
              Aggregation settings aren't editable in the new UI yet — they arrive in a later phase.
              Everything else on this page works as usual.
            </p>
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
                setDraft((d) => (d ? { ...d, validatorId: adapterId, validatorProperties: properties } : d))
              }
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
                setDraft((d) => (d ? { ...d, mapperId: adapterId, mapperProperties: properties } : d))
              }
              disabled={!canEdit}
              noneLabel="None — the document passes through unchanged"
              mapperEditorHref={`/subscriptions/${s.id}/mapper`}
            />
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
                setDraft((d) => (d ? { ...d, handlerId: adapterId, handlerProperties: properties } : d))
              }
              disabled={!canEdit}
              noneLabel="None — the document stops here"
            />
          </Panel>
        );
      case "response":
        return (
          <Panel title={label} description={description}>
            <ResponseFields
              handlerId={draft.handlerId}
              responseIntegrationId={draft.responseIntegrationId}
              responseMessageTypeName={draft.responseMessageTypeName}
              onChange={(patch) => setDraft((d) => (d ? { ...d, ...patch } : d))}
              disabled={!canEdit}
              candidates={(allIntegrations.data ?? []).filter((x) => x.id !== integrationId)}
              idPrefix="in-resp"
            />
          </Panel>
        );
    }
  };

  return (
    <div className="pb-24">
      <BackLink to="/subscriptions" label="Integrations" />

      <div className="mb-5 flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h1 className="flex flex-wrap items-center gap-2.5 text-[22px] font-semibold tracking-tight text-ink-900">
            <EditableTitle
              value={draft?.name ?? s.name}
              onChange={(v) => set("name", v)}
              disabled={!canEdit}
              placeholder="Integration name"
            />
            <TypeBadge type={s.type} />
            {draft && (
              <button
                type="button"
                disabled={!canEdit}
                onClick={() => set("enabled", !draft.enabled)}
                title="Disabled integrations are never scheduled or matched."
                className={`shrink-0 rounded-md px-2 py-0.5 text-xs font-medium disabled:cursor-not-allowed ${
                  draft.enabled ? "bg-ok-100 text-ok-600 hover:bg-ok-200/70" : "bg-ink-100 text-ink-700 hover:bg-ink-200"
                }`}
              >
                {draft.enabled ? "Active" : "Disabled"}
              </button>
            )}
            {paused && <Badge tone="warn">Paused</Badge>}
          </h1>
          <p className="mt-1 text-sm text-ink-500">
            Carries{" "}
            <Link to={`/information-types/${s.informationTypeId}`} className="hover:underline">
              <CodeBadge code={s.informationTypeCode} name={s.informationTypeName} className="align-middle" />
            </Link>
            .
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
            {canOperate && (
              <Button
                onClick={() => setConfirmingPause(true)}
                title={paused ? "Release held work and resume." : "Keep accepting work but hold it for later release."}
              >
                {paused ? <Play className="size-4" /> : <Pause className="size-4" />}
                {paused ? "Resume" : "Pause"}
              </Button>
            )}
            <Can permission="subscriptions.delete">
              <Button variant="danger" onClick={() => setDeleting(true)}>
                <Trash2 className="size-4" /> Delete
              </Button>
            </Can>
          </div>
          <FormError>{receive.error?.message}</FormError>
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
            canCreateWorkGroup={canCreateWorkGroup}
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
          title={paused ? "Resume this integration?" : "Pause this integration?"}
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

      {deleting && (
        <ConfirmDialog
          title="Delete this integration?"
          body={
            <>
              <strong className="font-medium text-ink-800">{s.name}</strong> and its configuration
              will be gone for good. One a gateway or another integration still points at can't be
              deleted — it will say which.
            </>
          }
          confirmLabel="Delete integration"
          onConfirm={async () => {
            await api.deleteIntegration(integrationId);
            void queryClient.invalidateQueries({ queryKey: ["integration-rows"] });
      void queryClient.invalidateQueries({ queryKey: ["integration-rows-search"] });
            void queryClient.invalidateQueries({ queryKey: ["integrations"] });
            navigate("/subscriptions");
          }}
          onClose={() => setDeleting(false)}
        />
      )}
    </div>
  );
}
