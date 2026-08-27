import { useState } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { X } from "lucide-react";
import { Button, FormError } from "../../components/ui/basics";
import { Checkbox, Field, TextInput } from "../../components/ui/forms";
import { Panel } from "../../components/ui/Panel";
import { SearchSelect } from "../../components/ui/SearchSelect";
import { AdapterConfig, useAdapterCatalog } from "../../components/config/AdapterConfig";
import { AggregationFields } from "../../components/config/AggregationFields";
import { ScheduleEditor } from "../../components/config/ScheduleEditor";
import { PartnerPicker } from "../../components/config/pickers";
import { useIntegrationsCache } from "../../components/config/shared";
import { api, type AggregationTarget, type Schedule } from "../../api";
import { STAGES, type StageId } from "../integrations/studio/stages";
import { StageRail } from "../integrations/studio/StageRail";
import { adapterIncomplete, faceOf } from "../integrations/studio/faces";
import { ResponseFields } from "../integrations/studio/ResponseFields";
import type { Draft as StudioDraft } from "../integrations/studio/model";
import { BackLink } from "../../components/ui/BackLink";

/** Local draft state with the patch-and-clear shape the other create pages use. */
function useDraft<T extends object>(initial: T) {
  const [draft, setDraft] = useState<T>(initial);
  const update = (patch: Partial<T>) => setDraft((d) => ({ ...d, ...patch }));
  return [draft, update] as const;
}

/** The whole pipeline — the same nodes the studio page edits for a saved aggregation. */
const STAGES_HERE: StageId[] = ["aggregation", "schedule", "transformation", "delivery", "response"];

type Draft = Pick<
  StudioDraft,
  | "name"
  | "schedules"
  | "aggregationTarget"
  | "mapperId"
  | "mapperProperties"
  | "handlerId"
  | "handlerProperties"
  | "responseIntegrationId"
  | "responseMessageTypeName"
> & {
  aggregationForId: number | null;
  partnerId: number | null;
  /**
   * Whether the partner shown is the person's own choice. Without this, "cleared it" and
   * "hasn't chosen yet" look identical and the source's partner keeps coming back.
   */
  partnerTouched: boolean;
  enable: boolean;
};

const EMPTY: Draft = {
  name: "",
  aggregationForId: null,
  partnerId: null,
  partnerTouched: false,
  aggregationTarget: "Input",
  // A sensible default so the node starts valid, edited through the full recurrence editor
  // on demand — matching the scheduled-job page. An aggregation has no other trigger, so a
  // draft that starts with none would open on a broken node.
  schedules: [{ recurrence: "Daily", days: 0, hours: 0, minutes: 0, backwards: false }] as Schedule[],
  mapperId: null,
  mapperProperties: {},
  handlerId: null,
  handlerProperties: {},
  responseIntegrationId: null,
  responseMessageTypeName: null,
  enable: false,
};

/**
 * Creating an aggregation, on the same pipeline the studio page edits — the scheduled-job
 * page's shape, for the same reason: you learn the rail once, and after Create you land on
 * the page you were already looking at.
 *
 * Arrives with `?source=` when started from the integration being rolled up, in which case
 * the source is fixed and shown rather than picked. Either way it is fixed after creation:
 * the backend's `AggregationForId` has a private setter and the configuration applier skips
 * it, so no update can repoint a live roll-up.
 */
export function NewAggregationPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [params] = useSearchParams();
  const fixedSourceId = params.get("source") ? Number(params.get("source")) : null;

  const [stage, setStage] = useState<StageId | null>("aggregation");

  const allIntegrations = useIntegrationsCache();
  const receivers = useAdapterCatalog("receiver");
  const validators = useAdapterCatalog("validator");
  const mappers = useAdapterCatalog("mapper");
  const handlers = useAdapterCatalog("handler");

  const [draft, update] = useDraft<Draft>({ ...EMPTY, aggregationForId: fixedSourceId });

  const source = allIntegrations.data?.find((s) => s.id === draft.aggregationForId) ?? null;

  // An aggregation may point at another aggregation — the backend does not stop it, and a
  // chain of roll-ups summarising roll-ups is not a shape anyone has asked for.
  const candidates = (allIntegrations.data ?? []).filter((s) => s.type !== "Aggregation");

  // The source's own partner where it has one, until the person says otherwise. It is a
  // suggestion rather than an inheritance: a Receiving source usually has no partner at all,
  // and the roll-up's partner answers a different question from the source's anyway.
  const partnerId = draft.partnerTouched ? draft.partnerId : (source?.partnerIds[0] ?? null);

  const create = useMutation({
    mutationFn: () =>
      api.createIntegration({
        type: "Aggregation",
        name: draft.name.trim(),
        // Ignored for this type — the backend forces the built-in Aggregation Document.
        informationTypeId: 0,
        partnerId,
        aggregationForId: draft.aggregationForId,
        aggregationTarget: draft.aggregationTarget,
        schedules: draft.schedules,
        mapperId: draft.mapperId,
        mapperProperties: draft.mapperProperties,
        handlerId: draft.handlerId,
        handlerProperties: draft.handlerProperties,
        responseIntegrationId: draft.responseIntegrationId,
        responseMessageTypeName: draft.responseMessageTypeName,
        enabled: draft.enable,
      }),
    onSuccess: (created) => {
      void queryClient.invalidateQueries();
      navigate(`/subscriptions/${created.id}`);
    },
  });

  // faceOf works off the studio's full draft shape; the fields this type never has are empty.
  const studioDraft: StudioDraft = {
    ...draft,
    enabled: draft.enable,
    workGroupId: null,
    retryPolicyId: null,
    receiverId: null,
    receiverProperties: {},
    validatorId: null,
    validatorProperties: {},
    matchExpression: null,
  };

  const faces = STAGES_HERE.map((id) =>
    faceOf(id, {
      type: "Aggregation",
      draft: studioDraft,
      catalogs: { receivers, validators, mappers, handlers },
      integrationNames: allIntegrations.data,
      aggregationForId: draft.aggregationForId,
      unsaved: true,
    }),
  );

  const unfilled = [
    adapterIncomplete(mappers, draft.mapperId, draft.mapperProperties) && "transformation",
    adapterIncomplete(handlers, draft.handlerId, draft.handlerProperties) && "delivery",
  ].filter((m): m is string => typeof m === "string");

  const missing = [
    draft.aggregationForId === null && "something to roll up",
    draft.name.trim().length < 2 && "a name",
    partnerId === null && "a partner",
    // As strict as the scheduled-job page, for the same reason: a roll-up with nowhere to go
    // is built and then thrown away. It also keeps the rail honest — the Delivery node reads
    // "Needed" while nothing is set, and that has to mean something.
    draft.handlerId === null && "a delivery",
    // Create does not demand a schedule, but update does — one saved without could never be
    // saved again from its own page. The surface that can make that mistake refuses to.
    draft.schedules.length === 0 && "a schedule",
    unfilled.length > 0 && `the required fields on ${unfilled.join(" and ")}`,
  ].filter((m): m is string => typeof m === "string");

  const renderStage = (stageId: StageId) => {
    const { label, description } = STAGES[stageId];
    switch (stageId) {
      case "aggregation":
        return (
          <Panel title={label} description={description}>
            <AggregationFields
              source={source}
              target={draft.aggregationTarget}
              onTargetChange={(aggregationTarget: AggregationTarget) => update({ aggregationTarget })}
            />
          </Panel>
        );
      case "schedule":
        return (
          <Panel title={label} description="When the source's exchanges are rolled up.">
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
              noneLabel="None — the list of links is delivered as it is"
            />
            <p className="mt-3 rounded-lg bg-ink-50 px-3 py-2 text-[13px] text-ink-500">
              What arrives here is a JSON list of links, not the documents themselves. Combining
              them into one file is this step's job, or the delivery's.
            </p>
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
          </Panel>
        );
      case "response":
        return (
          <Panel title={label} description={description}>
            <ResponseFields
              handlerId={draft.handlerId}
              responseIntegrationId={draft.responseIntegrationId}
              responseMessageTypeName={draft.responseMessageTypeName}
              onChange={update}
              disabled={false}
              candidates={allIntegrations.data ?? []}
              idPrefix="na-resp"
            />
          </Panel>
        );
      default:
        return null;
    }
  };

  return (
    <div className="pb-10">
      <BackLink to="/aggregations" label="Aggregations" />

      <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">New aggregation</h1>
      <p className="mt-1 mb-5 text-sm text-ink-500">
        On a schedule, collects another integration's successful exchanges into one exchange
        listing links to their files. It does not combine the files — the transformation or the
        delivery does that.
      </p>

      <div className="mb-5 flex flex-wrap gap-5">
        <div className="w-80">
          <Field label="Name" htmlFor="na-name">
            <TextInput
              id="na-name"
              value={draft.name}
              autoFocus
              placeholder={source ? `e.g. ${source.name} — daily manifest` : "e.g. Daily invoice manifest"}
              onChange={(e) => update({ name: e.target.value })}
            />
          </Field>
        </div>
        {fixedSourceId === null && (
          <div className="w-80">
            <Field
              label="Rolls up"
              htmlFor="na-source"
              hint="Fixed once created. An aggregation cannot roll up another aggregation."
            >
              <SearchSelect
                id="na-source"
                aria-label="Integration to roll up"
                value={draft.aggregationForId === null ? "" : String(draft.aggregationForId)}
                disabled={allIntegrations.isPending}
                onChange={(v) => v !== "" && update({ aggregationForId: Number(v) })}
                placeholder="Pick an integration…"
                options={candidates.map((s) => ({ value: String(s.id), label: s.name, hint: s.type }))}
              />
            </Field>
          </div>
        )}
        <div className="w-80">
          <Field
            label="Partner"
            htmlFor="na-partner"
            // The question this answers is not "whose exchanges are collected" — one roll-up
            // can sweep up exchanges belonging to many partners, or to none. It is who the
            // roll-up itself belongs to.
            hint="Who the roll-up exchange belongs to, and whose values fill {{partner.…}} in the delivery below. It does not affect which exchanges are collected."
          >
            <PartnerPicker
              id="na-partner"
              value={partnerId}
              onChange={(v) => update({ partnerId: v === "none" ? null : v, partnerTouched: true })}
            />
          </Field>
        </div>
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
          // Off by default, unlike a scheduled job: the first run of a new aggregation sweeps
          // up every successful exchange its source has ever produced, which for an old source
          // can be a very large first roll-up going straight to a partner.
          description="Off by default — the first run collects everything the source has ever produced, so enable it when you've checked the pipeline."
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
          <Button onClick={() => navigate("/aggregations")}>Cancel</Button>
          <Button
            variant="primary"
            busy={create.isPending}
            disabled={missing.length > 0}
            onClick={() => create.mutate()}
          >
            Create aggregation
          </Button>
        </div>
      </div>

      <FormError>{create.error?.message}</FormError>
    </div>
  );
}
