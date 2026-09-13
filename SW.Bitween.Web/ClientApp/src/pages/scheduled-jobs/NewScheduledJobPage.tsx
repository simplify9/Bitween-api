import { useState } from "react";
import { useNavigate } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { X } from "lucide-react";
import { Button, FormError } from "../../components/ui/basics";
import { Checkbox, Field, TextInput } from "../../components/ui/forms";
import { Panel } from "../../components/ui/Panel";
import {
  AdapterConfig,
  useAdapterCatalog,
} from "../../components/config/AdapterConfig";
import { ScheduleEditor } from "../../components/config/ScheduleEditor";
import { InfoTypePicker } from "../../components/config/pickers";
import { useSubscriptionsCache } from "../../components/config/shared";
import { api } from "../../api";
import { STAGES, type StageId } from "../subscriptions/studio/stages";
import { StageRail } from "../subscriptions/studio/StageRail";
import { adapterIncomplete, faceOf } from "../subscriptions/studio/faces";
import { ResponseFields } from "../subscriptions/studio/ResponseFields";
import type { Draft as StudioDraft } from "../subscriptions/studio/model";
import { BackLink } from "../../components/ui/BackLink";
import { DataSourceBinding } from "../subscriptions/studio/DataSourceBinding";
import { LaneAndRetry } from "../subscriptions/studio/LaneAndRetry";
import { useBindsToDataSource } from "../data-sources/providers";
import NativeMapperEditor from "../../components/nativeMapper/NativeMapperEditor";
import { NATIVE_MAPPER_ID } from "../../lib/nativeMapper/types";

/** Local draft state with the patch-and-clear shape the form bodies already use. */
function useDraft<T extends object>(initial: T) {
  const [draft, setDraft] = useState<T>(initial);
  const update = (patch: Partial<T>) => setDraft((d) => ({ ...d, ...patch }));
  const clear = () => setDraft(initial);
  return [draft, update, clear] as const;
}


/** The whole pipeline — same nodes the studio page edits. */
const STAGES_HERE: StageId[] = ["source", "schedule", "transformation", "delivery", "response"];

type Draft = Pick<
  StudioDraft,
  | "name"
  | "receiverId"
  | "receiverProperties"
  | "schedules"
  | "mapperId"
  | "mapperProperties"
  | "handlerId"
  | "handlerProperties"
  | "responseSubscriptionId"
  | "responseMessageTypeName"
  | "workGroupId"
  | "retryPolicyId"
  | "dataSourceId"
> & {
  informationTypeId: number | null;
  enable: boolean;
};

const EMPTY: Draft = {
  name: "",
  informationTypeId: null,
  receiverId: null,
  receiverProperties: {},
  // A sensible default so the node starts valid — edited through the full
  // recurrence editor on demand.
  schedules: [{ recurrence: "Daily", days: 0, hours: 0, minutes: 0, backwards: false }],
  mapperId: null,
  mapperProperties: {},
  handlerId: null,
  handlerProperties: {},
  responseSubscriptionId: null,
  responseMessageTypeName: null,
  workGroupId: null,
  retryPolicyId: null,
  dataSourceId: null,
  enable: true,
};

/**
 * Creating a scheduled job, on the same pipeline the studio page edits.
 *
 * This was a four-step wizard. It is the studio with empty nodes now, for one
 * reason: you learn the pipeline once, and after Create you land on the page you
 * were already looking at. Name and information type live above the diagram —
 * they identify the job rather than being a step in it.
 */
export function NewScheduledJobPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [stage, setStage] = useState<StageId | null>("source");
  /** The visual mapper, over the page — there is no subscription page to send you to yet. */
  const [mapping, setMapping] = useState(false);

  const allSubscriptions = useSubscriptionsCache();
  const receivers = useAdapterCatalog("receiver");
  const validators = useAdapterCatalog("validator");
  const mappers = useAdapterCatalog("mapper");
  const handlers = useAdapterCatalog("handler");

  const [draft, update, clear] = useDraft<Draft>(EMPTY);
  const bindsToDataSource = useBindsToDataSource();


  const create = useMutation({
    mutationFn: () =>
      api.createSubscription({
        type: "Receiving",
        name: draft.name,
        informationTypeId: draft.informationTypeId!,
        receiverId: draft.receiverId,
        receiverProperties: draft.receiverProperties,
        mapperId: draft.mapperId,
        mapperProperties: draft.mapperProperties,
        handlerId: draft.handlerId,
        handlerProperties: draft.handlerProperties,
        schedules: draft.schedules,
        responseSubscriptionId: draft.responseSubscriptionId,
        responseMessageTypeName: draft.responseMessageTypeName,
        workGroupId: draft.workGroupId,
        retryPolicyId: draft.retryPolicyId,
        dataSourceId: draft.dataSourceId,
        enabled: draft.enable,
      }),
    onSuccess: (created) => {
      clear();
      void queryClient.invalidateQueries();
      navigate(`/subscriptions/${created.id}`, { replace: true });
    },
  });

  // faceOf works off the studio's full draft shape; the fields this type never has
  // are simply empty.
  const studioDraft: StudioDraft = {
    ...draft,
    // Aggregation only, and this page never creates one.
    aggregationTarget: "Input",
    enabled: draft.enable,
    // Receiving has no Validation stage — see stages.ts.
    validatorId: null,
    validatorProperties: {},
    matchExpression: null,
  };

  const faces = STAGES_HERE.map((id) =>
    faceOf(id, {
      type: "Receiving",
      draft: studioDraft,
      catalogs: { receivers, validators, mappers, handlers },
      subscriptionNames: allSubscriptions.data,
    }),
  );

  // Deliberately strict: a job created without a source, a schedule or a
  // delivery would sit in the list looking active and never do anything.
  const unfilled = [
    adapterIncomplete(receivers, draft.receiverId, draft.receiverProperties) && "source",
    adapterIncomplete(mappers, draft.mapperId, draft.mapperProperties) && "transformation",
    adapterIncomplete(handlers, draft.handlerId, draft.handlerProperties) && "delivery",
  ].filter((m): m is string => typeof m === "string");

  const missing = [
    draft.name.trim().length < 2 && "a name",
    draft.informationTypeId === null && "an information type",
    draft.receiverId === null && "a source",
    draft.schedules.length === 0 && "a schedule",
    draft.handlerId === null && "a delivery",
    unfilled.length > 0 && `the required fields on ${unfilled.join(" and ")}`,
  ].filter((m): m is string => typeof m === "string");

  const renderStage = (stageId: StageId) => {
    const { label, description } = STAGES[stageId];
    switch (stageId) {
      case "source":
        return (
          <Panel title={label} description={description}>
            <AdapterConfig
              kind="receiver"
              adapterId={draft.receiverId}
              properties={draft.receiverProperties}
              onChange={(receiverId, receiverProperties) => update({ receiverId, receiverProperties })}
              disabled={false}
              required
            />
            {bindsToDataSource(draft.receiverId, "receiver") && (
              <div className="mt-3">
                <DataSourceBinding
                  slot="receiver"
                  siblings={[
                    { slot: "transformation", adapterId: draft.mapperId },
                    { slot: "delivery", adapterId: draft.handlerId },
                  ]}
                  dataSourceId={draft.dataSourceId}
                  properties={draft.receiverProperties}
                  onDataSourceChange={(dataSourceId) => update({ dataSourceId })}
                  onPropertiesChange={(receiverProperties) => update({ receiverProperties })}
                  disabled={false}
                />
              </div>
            )}
          </Panel>
        );
      case "schedule":
        return (
          <Panel title={label} description={description}>
            <ScheduleEditor
              schedules={draft.schedules}
              onChange={(schedules) => update({ schedules })}
              disabled={false}
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
              onChange={(mapperId, mapperProperties) => update({ mapperId, mapperProperties })}
              disabled={false}
              noneLabel="None — the document passes through unchanged"
              onOpenMapperEditor={() => setMapping(true)}
            />
            {bindsToDataSource(draft.mapperId, "mapper") && (
              <div className="mt-3">
                <DataSourceBinding
                  slot="mapper"
                  siblings={[
                    { slot: "source", adapterId: draft.receiverId },
                    { slot: "delivery", adapterId: draft.handlerId },
                  ]}
                  dataSourceId={draft.dataSourceId}
                  properties={draft.mapperProperties}
                  onDataSourceChange={(dataSourceId) => update({ dataSourceId })}
                  onPropertiesChange={(mapperProperties) => update({ mapperProperties })}
                  disabled={false}
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
              onChange={update}
              disabled={false}
              candidates={allSubscriptions.data ?? []}
              idPrefix="nj-resp"
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
              onChange={(handlerId, handlerProperties) => update({ handlerId, handlerProperties })}
              disabled={false}
              required
            />
            {bindsToDataSource(draft.handlerId, "handler") && (
              <div className="mt-3">
                <DataSourceBinding
                  slot="handler"
                  siblings={[
                    { slot: "source", adapterId: draft.receiverId },
                    { slot: "transformation", adapterId: draft.mapperId },
                  ]}
                  dataSourceId={draft.dataSourceId}
                  properties={draft.handlerProperties}
                  onDataSourceChange={(dataSourceId) => update({ dataSourceId })}
                  onPropertiesChange={(handlerProperties) => update({ handlerProperties })}
                  disabled={false}
                />
              </div>
            )}
          </Panel>
        );
      default:
        return null;
    }
  };

  return (
    <div className="pb-10">
      <BackLink to="/scheduled-jobs" label="Scheduled jobs" />

      <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">New scheduled job</h1>
      <p className="mt-1 mb-5 text-sm text-ink-500">
        Pulls documents in on a schedule and pushes them through a pipeline.
      </p>

      <div className="mb-5 flex flex-wrap gap-5">
        <div className="w-80">
          <Field label="Name" htmlFor="nj-name">
            <TextInput
              id="nj-name"
              value={draft.name}
              autoFocus
              placeholder="e.g. Acme purchase-order intake"
              onChange={(e) => update({ name: e.target.value })}
            />
          </Field>
        </div>
        <div className="w-80">
          <Field label="Carries" htmlFor="nj-type" hint="The information type this job pulls in.">
            <InfoTypePicker
              id="nj-type"
              value={draft.informationTypeId}
              onChange={(informationTypeId) => update({ informationTypeId })}
            />
          </Field>
        </div>
      </div>

      {/* The two settings that belong to no stage, in the strip the subscription's own
          page keeps them in. Offered here because the API has always accepted them on a
          create — leaving them out is what made a new job need a second visit. */}
      <div className="mb-5 flex flex-wrap items-start gap-x-10 gap-y-4 border-y border-ink-200 px-1 py-4">
        <LaneAndRetry
          workGroupId={draft.workGroupId}
          retryPolicyId={draft.retryPolicyId}
          onWorkGroupChange={(workGroupId) => update({ workGroupId })}
          onRetryPolicyChange={(retryPolicyId) => update({ retryPolicyId })}
          canEdit
          idPrefix="nj"
        />
      </div>

      <StageRail faces={faces} selected={stage} onSelect={setStage} />

      {stage !== null && (
        <div className="relative">
          {renderStage(stage)}
          <button
            type="button"
            onClick={() => setStage(null)}
            aria-label="Close this step"
            title="Close"
            className="absolute top-2.5 right-3 rounded-md p-1.5 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
          >
            <X className="size-4" />
          </button>
        </div>
      )}

      <div className="mt-5 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-ink-200 bg-white px-4 py-3">
        <Checkbox
          label="Enable immediately"
          description="Unchecked, the job is created disabled and can be enabled from its page."
          checked={draft.enable}
          onChange={(e) => update({ enable: e.target.checked })}
        />
        <div className="flex items-center gap-3">
          {missing.length > 0 && (
            <p className="text-[13px] text-ink-500">
              Still needs {missing.slice(0, -1).join(", ")}
              {missing.length > 1 ? " and " : ""}
              {missing.at(-1)}.
            </p>
          )}
          <Button onClick={() => navigate("/scheduled-jobs", { replace: true })}>Cancel</Button>
          <Button
            variant="primary"
            busy={create.isPending}
            disabled={missing.length > 0}
            onClick={() => create.mutate()}
          >
            Create job
          </Button>
        </div>
      </div>
      <FormError>{create.error?.message}</FormError>

      {/* Over the page rather than a route of its own: the job exists only in this
          component's state, so navigating to the editor would throw it away. Saving in
          there hands the rules back to the draft; they are written on Create. */}
      {mapping && (
        <NativeMapperEditor
          target={{
            kind: "draft",
            mapperId: draft.mapperId,
            mapperProperties: draft.mapperProperties,
            // A scheduled job carries no partner, so the preview substitutes none unless
            // one is picked in the editor's own "Preview as" list.
            partnerId: null,
            onSave: (mapperProperties) => update({ mapperId: NATIVE_MAPPER_ID, mapperProperties }),
          }}
          onClose={() => setMapping(false)}
        />
      )}
    </div>
  );
}
