import { useEffect, useState } from "react";
import { Link, Navigate, useNavigate, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, X } from "lucide-react";
import { api } from "../../api";
import { Button, FormError } from "../../components/ui/basics";
import { Checkbox, Field, TextInput } from "../../components/ui/forms";
import { CodeBadge, Panel } from "../../components/ui/Panel";
import { ReturnBanner } from "../../components/ui/ReturnBanner";
import { AdapterConfig, useAdapterCatalog } from "../../components/config/AdapterConfig";
import { InfoTypePicker } from "../../components/config/pickers";
import { useIntegrationsCache } from "../../components/config/shared";
import { usePersistentDraft } from "../../lib/persistentDraft";
import { takePicked, useHereAsReturnTarget, useReturnContext, withReturn } from "../../lib/returnTo";
import { STAGES, type StageId } from "./studio/stages";
import { StageRail } from "./studio/StageRail";
import { adapterIncomplete, faceOf } from "./studio/faces";
import { ResponseFields } from "./studio/ResponseFields";
import type { Draft as StudioDraft } from "./studio/model";

interface Draft {
  name: string;
  informationTypeId: number | null;
  validatorId: string | null;
  validatorProperties: Record<string, string>;
  mapperId: string | null;
  mapperProperties: Record<string, string>;
  handlerId: string | null;
  handlerProperties: Record<string, string>;
  responseIntegrationId: number | null;
  responseMessageTypeName: string | null;
  enable: boolean;
}

const EMPTY: Draft = {
  name: "",
  informationTypeId: null,
  validatorId: null,
  validatorProperties: {},
  mapperId: null,
  mapperProperties: {},
  handlerId: null,
  handlerProperties: {},
  responseIntegrationId: null,
  responseMessageTypeName: null,
  enable: true,
};

/**
 * Routed create page for gateway-backed integrations. Always reached as a detour
 * from an entry-point flow (attach partner / add route) — `type` and optionally
 * `informationTypeId` arrive as query params.
 *
 * Same pipeline canvas the studio page and the scheduled-job create page use,
 * with the same nodes — including Response, which the create call reaches
 * through the very same update the detail page saves through.
 *
 * Trigger is the one node that only explains itself: the attachment or route
 * that feeds this doesn't exist yet, and asking for the gateway and partner here
 * would repeat what the page you came from already asked.
 */
export function IntegrationNewPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const ctx = useReturnContext();
  const here = useHereAsReturnTarget();
  const [params, setParams] = useSearchParams();
  const [stage, setStage] = useState<StageId | null>("delivery");

  const type = params.get("type");
  const fixedInfoTypeId = params.get("informationTypeId") ? Number(params.get("informationTypeId")) : null;
  const infoTypes = useQuery({ queryKey: ["information-types"], queryFn: () => api.listInformationTypes() });

  const allIntegrations = useIntegrationsCache();
  const receivers = useAdapterCatalog("receiver");
  const validators = useAdapterCatalog("validator");
  const mappers = useAdapterCatalog("mapper");
  const handlers = useAdapterCatalog("handler");

  const [draft, update, clear] = usePersistentDraft<Draft>("bitween-draft-new-integration", EMPTY);

  // a detour to /information-types/new just came back with the new type
  useEffect(() => {
    const picked = takePicked(params, "infotype");
    if (picked !== null) {
      update({ informationTypeId: picked });
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev);
          next.delete("picked");
          return next;
        },
        { replace: true },
      );
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const create = useMutation({
    mutationFn: () =>
      api.createIntegration({
        type: type as "GatewayApiCall" | "BusGateway",
        name: draft.name,
        informationTypeId: fixedInfoTypeId ?? draft.informationTypeId!,
        // Not just because the node is hidden for bus gateways: the draft is
        // persisted under one key for both types, so a validator picked during
        // an abandoned API-gateway create would otherwise ride along invisibly
        // into a subscription that can never run it.
        validatorId: type === "BusGateway" ? null : draft.validatorId,
        validatorProperties: type === "BusGateway" ? {} : draft.validatorProperties,
        mapperId: draft.mapperId,
        mapperProperties: draft.mapperProperties,
        handlerId: draft.handlerId,
        handlerProperties: draft.handlerProperties,
        responseIntegrationId: draft.responseIntegrationId,
        responseMessageTypeName: draft.responseMessageTypeName,
        enabled: draft.enable,
      }),
    onSuccess: (created) => {
      clear();
      void queryClient.invalidateQueries();
      const base = `/subscriptions/${created.id}`;
      navigate(ctx ? `${withReturn(base, ctx)}&picked=integration:${created.id}` : base);
    },
  });

  if (type !== "GatewayApiCall" && type !== "BusGateway") return <Navigate to="/subscriptions" replace />;

  const fixedInfoType = fixedInfoTypeId ? infoTypes.data?.find((t) => t.id === fixedInfoTypeId) : undefined;

  /** No Validation for bus gateways — see the note in `stages.ts`. */
  const stages: StageId[] =
    type === "GatewayApiCall"
      ? ["trigger", "validation", "transformation", "delivery", "response"]
      : ["trigger", "transformation", "delivery", "response"];

  const studioDraft: StudioDraft = {
    ...draft,
    enabled: draft.enable,
    workGroupId: null,
    retryPolicyId: null,
    receiverId: null,
    receiverProperties: {},
    matchExpression: null,
    schedules: [],
  };

  // Whoever sent us here owns the trigger and named it, e.g.
  // "Acme partner API -> Northwind Foods".
  const triggerHint = params.get("trigger");
  // Without a return context nothing is going to wire this up when we finish —
  // reached by hand-typed URL now that the Integrations page has no create
  // button. Say so instead of promising a return that isn't coming.
  const willBeWired = ctx !== null;

  const faces = stages.map((id) =>
    // Trigger is overridden rather than taken from `faceOf`: with no entry
    // points yet that would read "Not wired up" in red, and when a return
    // context exists nothing is wrong — the wiring is the next step, not a
    // missing one.
    id === "trigger"
      ? {
          id,
          title: willBeWired ? "Wired up next" : "Not wired up",
          detail:
            triggerHint ??
            (willBeWired
              ? type === "GatewayApiCall"
                ? "a gateway attachment"
                : "a bus route"
              : undefined),
          state: (willBeWired ? "none" : "missing") as "none" | "missing",
          dirty: false,
          readOnly: true,
        }
      : faceOf(id, {
          type,
          draft: studioDraft,
          catalogs: { receivers, validators, mappers, handlers },
        }),
  );

  const unfilled = [
    type === "GatewayApiCall" &&
      adapterIncomplete(validators, draft.validatorId, draft.validatorProperties) &&
      "validation",
    adapterIncomplete(mappers, draft.mapperId, draft.mapperProperties) && "transformation",
    adapterIncomplete(handlers, draft.handlerId, draft.handlerProperties) && "delivery",
  ].filter((m): m is string => typeof m === "string");

  const missing = [
    draft.name.trim().length < 2 && "a name",
    fixedInfoTypeId === null && draft.informationTypeId === null && "an information type",
    draft.handlerId === null && "a delivery",
    unfilled.length > 0 && `the required fields on ${unfilled.join(" and ")}`,
  ].filter((m): m is string => typeof m === "string");

  const renderStage = (stageId: StageId) => {
    const { label, description } = STAGES[stageId];
    switch (stageId) {
      case "trigger":
        return (
          <Panel title={label} description={description}>
            <p className="text-sm text-ink-500">
              {type === "GatewayApiCall"
                ? "A partner calling an API gateway is what sets this off."
                : "A message on the bus matching a route is what sets this off."}{" "}
              {willBeWired ? (
                <>
                  {triggerHint ? (
                    <>
                      This one will run for{" "}
                      <strong className="font-medium text-ink-700">{triggerHint}</strong>, wired up
                      the moment the integration exists —{" "}
                    </>
                  ) : (
                    <>
                      The {type === "GatewayApiCall" ? "attachment" : "route"} is made right after
                      this integration exists —{" "}
                    </>
                  )}
                  you'll be taken back to finish it.
                </>
              ) : (
                <>
                  Nothing is going to connect this one: you got here directly rather than from a
                  gateway. It will be created unwired, and you'll need to attach it from an{" "}
                  {type === "GatewayApiCall" ? "API gateway" : "bus gateway"} page afterwards.
                </>
              )}
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
              onChange={(validatorId, validatorProperties) => update({ validatorId, validatorProperties })}
              disabled={false}
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
              onChange={(mapperId, mapperProperties) => update({ mapperId, mapperProperties })}
              disabled={false}
              noneLabel="None — the document passes through unchanged"
            />
            {draft.mapperId === "NativeJSONMapper" && (
              <p className="mt-3 rounded-lg bg-ink-50 px-3 py-2 text-[13px] text-ink-500">
                The visual mapping editor opens from the integration's own page, once it exists.
              </p>
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
              idPrefix="ni-resp"
            />
          </Panel>
        );
      default:
        return null;
    }
  };

  return (
    <div className="pb-10">
      <Link
        to={ctx?.to ?? "/subscriptions"}
        className="mb-4 inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800"
      >
        <ArrowLeft className="size-3.5" /> {ctx ? "Back without creating" : "Integrations"}
      </Link>

      <ReturnBanner />

      <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">New integration</h1>
      <p className="mt-1 mb-5 text-sm text-ink-500">
        {type === "GatewayApiCall"
          ? "Runs when a partner calls an API gateway."
          : "Runs when a bus gateway route matches a message."}
      </p>

      <div className="mb-5 flex flex-wrap gap-5">
        <div className="w-80">
          <Field label="Name" htmlFor="ni-name">
            <TextInput
              id="ni-name"
              value={draft.name}
              autoFocus
              placeholder="e.g. Coral orders intake"
              onChange={(e) => update({ name: e.target.value })}
            />
          </Field>
        </div>
        <div className="w-80">
          {fixedInfoTypeId !== null ? (
            <Field label="Carries" hint="Fixed by the bus gateway this integration will route from.">
              <p className="flex h-9.5 items-center text-sm text-ink-600">
                {fixedInfoType ? <CodeBadge code={fixedInfoType.code} name={fixedInfoType.name} /> : "…"}
              </p>
            </Field>
          ) : (
            <Field label="Carries" htmlFor="ni-type" hint="The information type this integration processes.">
              <InfoTypePicker
                id="ni-type"
                value={draft.informationTypeId}
                onChange={(informationTypeId) => update({ informationTypeId })}
                detourCtx={{ to: here, label: "Creating a new integration" }}
              />
            </Field>
          )}
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
          description="Unchecked, the integration is created disabled and can be enabled from its page."
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
          <Button onClick={() => navigate(ctx?.to ?? "/subscriptions")}>Cancel</Button>
          <Button
            variant="primary"
            busy={create.isPending}
            disabled={missing.length > 0}
            onClick={() => create.mutate()}
          >
            Create integration
          </Button>
        </div>
      </div>
      <FormError>{create.error?.message}</FormError>
    </div>
  );
}
