import { useState } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { X } from "lucide-react";
import { Button, FormError } from "../../components/ui/basics";
import { Checkbox, Field, TextInput } from "../../components/ui/forms";
import { Panel } from "../../components/ui/Panel";
import { AdapterConfig, useAdapterCatalog } from "../../components/config/AdapterConfig";
import { InfoTypePicker } from "../../components/config/pickers";
import { useSubscriptionsCache } from "../../components/config/shared";
import { api } from "../../api";
import { STAGES, stagesFor, type StageId } from "../subscriptions/studio/stages";
import { StageRail } from "../subscriptions/studio/StageRail";
import { adapterIncomplete, faceOf } from "../subscriptions/studio/faces";
import { ResponseFields } from "../subscriptions/studio/ResponseFields";
import { ResponseTrigger } from "../subscriptions/studio/ResponseTrigger";
import type { Draft as StudioDraft } from "../subscriptions/studio/model";
import { BackLink } from "../../components/ui/BackLink";
import { DataSourceBinding } from "../subscriptions/studio/DataSourceBinding";
import { LaneAndRetry } from "../subscriptions/studio/LaneAndRetry";
import { useBindsToDataSource } from "../data-sources/providers";
import NativeMapperEditor from "../../components/nativeMapper/NativeMapperEditor";
import { NATIVE_MAPPER_ID } from "../../lib/nativeMapper/types";
import { returnPath, safeReturn, useResponseDetour } from "../../lib/responseDetour";

const STAGES_HERE = stagesFor("Response");

type Draft = Pick<
  StudioDraft,
  | "name"
  | "mapperId"
  | "mapperProperties"
  | "handlerId"
  | "handlerProperties"
  | "responseSubscriptionId"
  | "responseMessageTypeName"
  | "runOnBadResponses"
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
  mapperId: null,
  mapperProperties: {},
  handlerId: null,
  handlerProperties: {},
  responseSubscriptionId: null,
  responseMessageTypeName: null,
  runOnBadResponses: false,
  workGroupId: null,
  retryPolicyId: null,
  dataSourceId: null,
  enable: true,
};

/**
 * Creating a response subscription, on the same pipeline the studio page edits.
 *
 * Nothing feeds it yet, and nothing here can make something feed it: that is chosen in the
 * feeding subscription's own Response step, where the response is. Opened from that step it
 * arrives with `?return=`, and Create goes back there with the new one picked; opened on its
 * own, Create lands on its page, whose Trigger node lists the feeders once there are any.
 *
 * Keyed by where it goes back to: a new one started from this page is this same route, and
 * without a key the draft on screen would carry over into it — and back again.
 */
export function NewResponseSubscriptionPage() {
  const [params] = useSearchParams();
  return <NewResponseSubscription key={params.get("return") ?? ""} />;
}

function NewResponseSubscription() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [params] = useSearchParams();
  const returnTo = safeReturn(params.get("return"));
  // It can hand its own response to a new response subscription too, which is this page again.
  const detour = useResponseDetour<Draft>();
  const [stage, setStage] = useState<StageId | null>(
    detour.kept || detour.pickedResponse !== null ? "response" : "delivery",
  );
  /** The visual mapper, over the page — there is no subscription page to send you to yet. */
  const [mapping, setMapping] = useState(false);

  const allSubscriptions = useSubscriptionsCache();
  const validators = useAdapterCatalog("validator");
  const mappers = useAdapterCatalog("mapper");
  const handlers = useAdapterCatalog("handler");

  const [draft, setDraft] = useState<Draft>(() => ({
    ...(detour.kept ?? EMPTY),
    ...(detour.pickedResponse !== null ? { responseSubscriptionId: detour.pickedResponse } : {}),
  }));
  const update = (patch: Partial<Draft>) => setDraft((d) => ({ ...d, ...patch }));
  const bindsToDataSource = useBindsToDataSource();

  const create = useMutation({
    mutationFn: () =>
      api.createSubscription({
        type: "Response",
        name: draft.name,
        informationTypeId: draft.informationTypeId!,
        mapperId: draft.mapperId,
        mapperProperties: draft.mapperProperties,
        handlerId: draft.handlerId,
        handlerProperties: draft.handlerProperties,
        responseSubscriptionId: draft.responseSubscriptionId,
        responseMessageTypeName: draft.responseMessageTypeName,
        runOnBadResponses: draft.runOnBadResponses,
        workGroupId: draft.workGroupId,
        retryPolicyId: draft.retryPolicyId,
        dataSourceId: draft.dataSourceId,
        enabled: draft.enable,
      }),
    onSuccess: async (created) => {
      // Awaited when going back: the page there picks it from the subscriptions list, which
      // has to include it by then.
      await queryClient.invalidateQueries();
      navigate(returnTo ? returnPath(returnTo, created.id) : `/subscriptions/${created.id}`, { replace: true });
    },
  });

  // faceOf works off the studio's full draft shape; the fields this type never has
  // are simply empty.
  const studioDraft: StudioDraft = {
    ...draft,
    enabled: draft.enable,
    aggregationTarget: "Input",
    receiverId: null,
    receiverProperties: {},
    validatorId: null,
    validatorProperties: {},
    matchExpression: null,
    schedules: [],
  };

  const faces = STAGES_HERE.map((id) => {
    const face = faceOf(id, {
      type: "Response",
      draft: studioDraft,
      catalogs: { receivers: { data: undefined }, validators, mappers, handlers },
      subscriptionNames: allSubscriptions.data,
      unsaved: true,
    });
    // Nothing can feed it before it exists, so "Nothing feeds it" is not a fault yet.
    return id === "trigger" ? { ...face, state: "none" as const } : face;
  });

  const unfilled = [
    adapterIncomplete(mappers, draft.mapperId, draft.mapperProperties) && "transformation",
    adapterIncomplete(handlers, draft.handlerId, draft.handlerProperties) && "delivery",
  ].filter((m): m is string => typeof m === "string");

  const missing = [
    draft.name.trim().length < 2 && "a name",
    draft.informationTypeId === null && "an information type",
    draft.handlerId === null && "a delivery",
    unfilled.length > 0 && `the required fields on ${unfilled.join(" and ")}`,
  ].filter((m): m is string => typeof m === "string");

  const renderStage = (stageId: StageId) => {
    const { label, description } = STAGES[stageId];
    switch (stageId) {
      case "trigger":
        return (
          <Panel title={label} description={description}>
            <ResponseTrigger
              feeders={[]}
              runOnBadResponses={draft.runOnBadResponses}
              onChange={(runOnBadResponses) => update({ runOnBadResponses })}
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
              noneLabel="None — the response passes through unchanged"
              onOpenMapperEditor={() => setMapping(true)}
            />
            {bindsToDataSource(draft.mapperId, "mapper") && (
              <div className="mt-3">
                <DataSourceBinding
                  slot="mapper"
                  siblings={[{ slot: "delivery", adapterId: draft.handlerId }]}
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
                  siblings={[{ slot: "transformation", adapterId: draft.mapperId }]}
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
              idPrefix="nr-resp"
              onNewResponseSubscription={() => detour.leave(draft)}
              onOpenResponseSubscription={(id) => detour.open(draft, id)}
            />
          </Panel>
        );
      default:
        return null;
    }
  };

  return (
    <div className="pb-10">
      {returnTo ? (
        <BackLink to={returnTo} label="Back" />
      ) : (
        <BackLink to="/response-subscriptions" label="Response subscriptions" />
      )}

      <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">New response subscription</h1>
      <p className="mt-1 mb-5 text-sm text-ink-500">
        Runs on what another subscription's delivery hands back, as that subscription's partner.
        {returnTo && " Create takes you back with it picked, and nothing you had there is lost."}
      </p>

      <div className="mb-5 flex flex-wrap gap-5">
        <div className="w-80">
          <Field label="Name" htmlFor="nr-name">
            <TextInput
              id="nr-name"
              value={draft.name}
              autoFocus
              placeholder="e.g. Store Acme shipment labels"
              onChange={(e) => update({ name: e.target.value })}
            />
          </Field>
        </div>
        <div className="w-80">
          <Field label="Carries" htmlFor="nr-type" hint="The information type of the responses it runs on.">
            <InfoTypePicker
              id="nr-type"
              value={draft.informationTypeId}
              onChange={(informationTypeId) => update({ informationTypeId })}
            />
          </Field>
        </div>
      </div>

      <div className="mb-5 flex flex-wrap items-start gap-x-10 gap-y-4 border-y border-ink-200 px-1 py-4">
        <LaneAndRetry
          workGroupId={draft.workGroupId}
          retryPolicyId={draft.retryPolicyId}
          onWorkGroupChange={(workGroupId) => update({ workGroupId })}
          onRetryPolicyChange={(retryPolicyId) => update({ retryPolicyId })}
          canEdit
          idPrefix="nr"
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
          description="Unchecked, it is created disabled and skips every response handed to it until enabled."
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
          <Button onClick={() => navigate(returnTo ?? "/response-subscriptions", { replace: true })}>
            Cancel
          </Button>
          <Button
            variant="primary"
            busy={create.isPending}
            disabled={missing.length > 0}
            onClick={() => create.mutate()}
          >
            Create response subscription
          </Button>
        </div>
      </div>
      <FormError>{create.error?.message}</FormError>

      {/* Over the page rather than a route of its own: the subscription exists only in this
          component's state, so navigating to the editor would throw it away. */}
      {mapping && (
        <NativeMapperEditor
          target={{
            kind: "draft",
            mapperId: draft.mapperId,
            mapperProperties: draft.mapperProperties,
            // It runs as whichever partner fed it, so there is no one partner to preview as
            // unless one is picked in the editor's own "Preview as" list.
            partnerId: null,
            onSave: (mapperProperties) => update({ mapperId: NATIVE_MAPPER_ID, mapperProperties }),
          }}
          onClose={() => setMapping(false)}
        />
      )}
    </div>
  );
}
