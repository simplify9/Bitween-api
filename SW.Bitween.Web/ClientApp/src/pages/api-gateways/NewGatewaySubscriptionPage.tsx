import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { X } from "lucide-react";
import { api } from "../../api";
import { Button, EmptyState, FormError, LoadingBlock } from "../../components/ui/basics";
import { Checkbox, Field, TextInput } from "../../components/ui/forms";
import { Panel } from "../../components/ui/Panel";
import {
  AdapterConfig,
  useAdapterCatalog,
} from "../../components/config/AdapterConfig";
import { InfoTypePicker } from "../../components/config/pickers";
import { DataSourceBinding } from "../subscriptions/studio/DataSourceBinding";
import { LaneAndRetry } from "../subscriptions/studio/LaneAndRetry";
import { useBindsToDataSource } from "../data-sources/providers";
import NativeMapperEditor from "../../components/nativeMapper/NativeMapperEditor";
import { NATIVE_MAPPER_ID } from "../../lib/nativeMapper/types";
import { useSubscriptionsCache } from "../../components/config/shared";
import { STAGES, stagesFor, type StageId } from "../subscriptions/studio/stages";
import { StageRail } from "../subscriptions/studio/StageRail";
import { EntryPointsTable } from "../subscriptions/studio/Overview";
import { adapterIncomplete, faceOf } from "../subscriptions/studio/faces";
import { ResponseFields } from "../subscriptions/studio/ResponseFields";
import type { Draft as StudioDraft } from "../subscriptions/studio/model";
import { BackLink } from "../../components/ui/BackLink";
import { keys } from "../../api/queryKeys";

/** Local draft state with the patch-and-clear shape the form bodies already use. */
function useDraft<T extends object>(initial: T) {
  const [draft, setDraft] = useState<T>(initial);
  const update = (patch: Partial<T>) => setDraft((d) => ({ ...d, ...patch }));
  return [draft, update] as const;
}

type Draft = Pick<
  StudioDraft,
  | "name"
  | "validatorId"
  | "validatorProperties"
  | "mapperId"
  | "mapperProperties"
  | "handlerId"
  | "handlerProperties"
  | "responseSubscriptionId"
  | "responseMessageTypeName"
  | "enabled"
  | "workGroupId"
  | "retryPolicyId"
  | "dataSourceId"
> & { informationTypeId: number | null };

const EMPTY: Draft = {
  name: "",
  informationTypeId: null,
  validatorId: null,
  validatorProperties: {},
  mapperId: null,
  mapperProperties: {},
  handlerId: null,
  handlerProperties: {},
  responseSubscriptionId: null,
  responseMessageTypeName: null,
  // Live as soon as it exists: it only runs once a partner is attached to it, which is
  // the thing you are in the middle of doing.
  enabled: true,
  workGroupId: null,
  retryPolicyId: null,
  dataSourceId: null,
};

const STAGES_HERE = stagesFor("GatewayApiCall");

/**
 * Creating the subscription a partner attachment points at.
 *
 * A dialog here used to ask only for a name, a type and a delivery — the least an
 * attachment can't exist without. That made it a worse copy of the subscription's
 * own page for anyone who wanted more. This is that same page's pipeline instead,
 * routed rather than saved-then-configured: nothing points at the subscription
 * until the attachment page does, so there is nothing to leave half-wired if you
 * stop after creating it, and the return trip lands you back there with it
 * already picked.
 *
 * It offers every setting the subscription's own page does, because the create call
 * has always accepted them all. What it used to leave out — the lane, the retry
 * policy, the connection, the mapping — was the whole reason a new subscription had
 * to be opened a second time to finish it.
 */
export function NewGatewaySubscriptionPage() {
  const { id = "" } = useParams();
  const gatewayId = Number(id);
  const [searchParams] = useSearchParams();
  const partnerId = searchParams.get("partnerId");
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [stage, setStage] = useState<StageId | null>("delivery");
  /** The visual mapper, over the page — there is no subscription page to send you to yet. */
  const [mapping, setMapping] = useState(false);

  const gateway = useQuery({
    queryKey: keys.apiGateways.detail(gatewayId),
    queryFn: () => api.getApiGateway(gatewayId),
    retry: false,
  });
  const allSubscriptions = useSubscriptionsCache();
  const validators = useAdapterCatalog("validator");
  const mappers = useAdapterCatalog("mapper");
  const handlers = useAdapterCatalog("handler");

  const [draft, update] = useDraft<Draft>(EMPTY);
  const bindsToDataSource = useBindsToDataSource();

  /**
   * Fills the name in from the gateway, so the field is one to accept rather than one
   * to invent. Deliberately not from the partner in `?partnerId=`, even though it is
   * right there: an attachment is (gateway, partner, subscription), and one subscription
   * is normally shared by every partner on the gateway. Seeding it with whichever partner
   * happened to be picked first would name a shared pipeline after one of its callers.
   *
   * Seeded once: `touched` latches as soon as the field is edited, so nothing overwrites
   * a typed name.
   */
  const touched = useRef(false);
  const gatewayName = gateway.data?.name;
  useEffect(() => {
    if (touched.current || !gatewayName) return;
    update({ name: gatewayName });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [gatewayName]);

  /** Back to the attach page, carrying the partner along if one was already picked. */
  const backToAttach = (extra: Record<string, string>) => {
    const query = new URLSearchParams(extra);
    if (partnerId) query.set("partnerId", partnerId);
    const qs = query.toString();
    navigate(`/api-gateways/${gatewayId}/attach${qs ? `?${qs}` : ""}`, { replace: true });
  };

  const create = useMutation({
    mutationFn: () =>
      api.createSubscription({
        type: "GatewayApiCall",
        name: draft.name.trim(),
        informationTypeId: draft.informationTypeId!,
        validatorId: draft.validatorId,
        validatorProperties: draft.validatorProperties,
        mapperId: draft.mapperId,
        mapperProperties: draft.mapperProperties,
        handlerId: draft.handlerId,
        handlerProperties: draft.handlerProperties,
        responseSubscriptionId: draft.responseSubscriptionId,
        responseMessageTypeName: draft.responseMessageTypeName,
        workGroupId: draft.workGroupId,
        retryPolicyId: draft.retryPolicyId,
        dataSourceId: draft.dataSourceId,
        enabled: draft.enabled,
      }),
    onSuccess: (created) => {
      void queryClient.invalidateQueries({ queryKey: keys.subscriptions.all });
      backToAttach({ picked: String(created.id) });
    },
  });

  if (gateway.isPending) return <LoadingBlock label="Loading gateway…" />;
  if (gateway.isError)
    return (
      <EmptyState title="This API gateway no longer exists">
        <Link to="/api-gateways" className="font-medium text-crimson-700 hover:underline">
          Back to API gateways
        </Link>
      </EmptyState>
    );

  const g = gateway.data;

  // faceOf works off the studio's full draft shape; the fields this page can't
  // set (there is no receiver, schedule or filter on this type) are simply empty.
  const studioDraft: StudioDraft = {
    ...draft,
    // Aggregation only, and this page never creates one.
    aggregationTarget: "Input",
    receiverId: null,
    receiverProperties: {},
    matchExpression: null,
    schedules: [],
  };

  const faces = STAGES_HERE.map((stageId) => {
    const face = faceOf(stageId, {
      type: "GatewayApiCall",
      draft: studioDraft,
      catalogs: { receivers: { data: undefined }, validators, mappers, handlers },
      subscriptionNames: allSubscriptions.data,
    });
    // Nothing can be wired up before the subscription exists, so the "missing"
    // reading faceOf gives a saved-but-unattached subscription would just be
    // noise here — dashed and flat instead, like every other node this page
    // can't offer real configuration for.
    return stageId === "trigger" ? { ...face, readOnly: true, state: "none" as const } : face;
  });

  const missing = [
    draft.name.trim().length < 2 && "a name",
    draft.informationTypeId === null && "the information type it carries",
    draft.handlerId === null && "a delivery",
    adapterIncomplete(handlers, draft.handlerId, draft.handlerProperties) && "its required delivery fields",
  ].filter((m): m is string => typeof m === "string");

  const renderStage = (stageId: StageId) => {
    const { label, description } = STAGES[stageId];
    switch (stageId) {
      case "trigger":
        return (
          <Panel title={label} description={description}>
            <EntryPointsTable
              rows={[]}
              empty="Not wired into any gateway yet — attach it to a partner once it's created."
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
              onChange={(validatorId, validatorProperties) => update({ validatorId, validatorProperties })}
              disabled={false}
              noneLabel="None — accepts everything"
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
              idPrefix="ngi-resp"
            />
          </Panel>
        );
      default:
        return null;
    }
  };

  return (
    <div className="pb-10">
      <BackLink to={`/api-gateways/${gatewayId}/attach`} label="Attaching a partner" />

      <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">
        New subscription for {g.name}
      </h1>
      <p className="mt-1 mb-5 text-sm text-ink-500">
        Everything it needs, in one go — nothing here has to wait until after it is created.
      </p>

      <div className="mb-5 flex flex-wrap gap-5">
        <div className="w-80">
          <Field label="Name" htmlFor="ngi-name">
            <TextInput
              id="ngi-name"
              value={draft.name}
              autoFocus
              placeholder="e.g. Coral orders to SAP"
              onChange={(e) => {
                touched.current = true;
                update({ name: e.target.value });
              }}
            />
          </Field>
        </div>
        <div className="w-80">
          <Field
            label="Carries"
            htmlFor="ngi-type"
            hint="An API gateway isn't tied to one, so this subscription names it."
          >
            <InfoTypePicker
              id="ngi-type"
              value={draft.informationTypeId}
              onChange={(informationTypeId) => update({ informationTypeId })}
            />
          </Field>
        </div>
      </div>

      {/* The two settings that belong to no stage, in the strip the subscription's own
          page keeps them in. Offered here because the API has always accepted them on a
          create — leaving them out is what made a new subscription need a second visit. */}
      <div className="mb-5 flex flex-wrap items-start gap-x-10 gap-y-4 border-y border-ink-200 px-1 py-4">
        <LaneAndRetry
          workGroupId={draft.workGroupId}
          retryPolicyId={draft.retryPolicyId}
          onWorkGroupChange={(workGroupId) => update({ workGroupId })}
          onRetryPolicyChange={(retryPolicyId) => update({ retryPolicyId })}
          canEdit
          idPrefix="ngi"
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
          description="Unchecked, it is created disabled. Either way it only runs once a partner is attached."
          checked={draft.enabled}
          onChange={(e) => update({ enabled: e.target.checked })}
        />
        <div className="flex flex-wrap items-center gap-3">
          {missing.length > 0 && (
            <p className="text-[13px] text-ink-500">
              Still needs {missing.slice(0, -1).join(", ")}
              {missing.length > 1 ? " and " : ""}
              {missing.at(-1)}.
            </p>
          )}
          <Button onClick={() => backToAttach({})}>Cancel</Button>
          <Button
            variant="primary"
            busy={create.isPending}
            disabled={missing.length > 0}
            onClick={() => create.mutate()}
          >
            Create subscription
          </Button>
        </div>
      </div>
      <FormError>{create.error?.message}</FormError>

      {/* Over the page rather than a route of its own: the subscription exists only in
          this component's state, so navigating to the editor would throw it away. Saving
          in there hands the rules back to the draft; they are written on Create. */}
      {mapping && (
        <NativeMapperEditor
          target={{
            kind: "draft",
            mapperId: draft.mapperId,
            mapperProperties: draft.mapperProperties,
            partnerId: partnerId === null ? null : Number(partnerId),
            onSave: (mapperProperties) => update({ mapperId: NATIVE_MAPPER_ID, mapperProperties }),
          }}
          onClose={() => setMapping(false)}
        />
      )}
    </div>
  );
}
