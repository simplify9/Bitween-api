import { useEffect } from "react";
import { Link, Navigate, useNavigate, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import { api } from "../../api";
import { Button, FormError } from "../../components/ui/basics";
import { Checkbox, Field, TextInput } from "../../components/ui/forms";
import { CodeBadge } from "../../components/ui/Panel";
import { ReturnBanner } from "../../components/ui/ReturnBanner";
import { AdapterConfig } from "../../components/config/AdapterConfig";
import { InfoTypePicker } from "../../components/config/pickers";
import { usePersistentDraft } from "../../components/config/wizard";
import { takePicked, useHereAsReturnTarget, useReturnContext, withReturn } from "../../lib/returnTo";

interface Draft {
  name: string;
  informationTypeId: number | null;
  validatorId: string | null;
  validatorProperties: Record<string, string>;
  mapperId: string | null;
  mapperProperties: Record<string, string>;
  handlerId: string | null;
  handlerProperties: Record<string, string>;
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
  enable: true,
};

/**
 * Routed create page for gateway-backed integrations. Always reached as a
 * detour from an entry-point flow (attach partner / add route) — `type` and
 * optionally `informationTypeId` arrive as query params.
 */
export function IntegrationNewPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const ctx = useReturnContext();
  const here = useHereAsReturnTarget();
  const [params, setParams] = useSearchParams();

  const type = params.get("type");
  const fixedInfoTypeId = params.get("informationTypeId") ? Number(params.get("informationTypeId")) : null;
  const infoTypes = useQuery({ queryKey: ["information-types"], queryFn: () => api.listInformationTypes() });

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
        validatorId: draft.validatorId,
        validatorProperties: draft.validatorProperties,
        mapperId: draft.mapperId,
        mapperProperties: draft.mapperProperties,
        handlerId: draft.handlerId,
        handlerProperties: draft.handlerProperties,
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
  const valid =
    draft.name.trim().length >= 2 &&
    (fixedInfoTypeId !== null || draft.informationTypeId !== null) &&
    draft.handlerId !== null;

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
      <p className="mt-1 text-sm text-ink-500">
        {type === "GatewayApiCall"
          ? "Runs when a partner calls an API gateway."
          : "Runs when a bus gateway route matches a message."}
      </p>

      <div className="mt-6 max-w-3xl space-y-6 rounded-xl border border-ink-200 bg-white p-5">
        <div className="max-w-sm">
          <Field label="Integration name" htmlFor="ni-name">
            <TextInput
              id="ni-name"
              value={draft.name}
              autoFocus
              placeholder="e.g. Coral orders intake"
              onChange={(e) => update({ name: e.target.value })}
            />
          </Field>
        </div>

        <section>
          <h2 className="text-[15px] font-semibold text-ink-900">Carries</h2>
          {fixedInfoTypeId !== null ? (
            <p className="mt-2 flex items-center gap-2 text-sm text-ink-600">
              {fixedInfoType ? <CodeBadge code={fixedInfoType.code} /> : "…"} — fixed by the bus
              gateway this integration will route from.
            </p>
          ) : (
            <>
              <p className="mb-3 text-[13px] text-ink-500">The information type this integration processes.</p>
              <InfoTypePicker
                value={draft.informationTypeId}
                onChange={(id) => update({ informationTypeId: id })}
                detourCtx={{ to: here, label: "Creating a new integration" }}
              />
            </>
          )}
        </section>

        <section>
          <h2 className="text-[15px] font-semibold text-ink-900">Validation</h2>
          <p className="mb-3 text-[13px] text-ink-500">Rejects bad documents at the door. Optional.</p>
          <AdapterConfig
            kind="validator"
            adapterId={draft.validatorId}
            properties={draft.validatorProperties}
            onChange={(validatorId, validatorProperties) => update({ validatorId, validatorProperties })}
            disabled={false}
            noneLabel="None — accept every document"
          />
        </section>

        <section>
          <h2 className="text-[15px] font-semibold text-ink-900">Transformation</h2>
          <p className="mb-3 text-[13px] text-ink-500">Reshapes the document before delivery. Optional.</p>
          <AdapterConfig
            kind="mapper"
            adapterId={draft.mapperId}
            properties={draft.mapperProperties}
            onChange={(mapperId, mapperProperties) => update({ mapperId, mapperProperties })}
            disabled={false}
            noneLabel="None — the document passes through unchanged"
          />
        </section>

        <section>
          <h2 className="text-[15px] font-semibold text-ink-900">Delivery</h2>
          <p className="mb-3 text-[13px] text-ink-500">Where the document ends up.</p>
          <AdapterConfig
            kind="handler"
            adapterId={draft.handlerId}
            properties={draft.handlerProperties}
            onChange={(handlerId, handlerProperties) => update({ handlerId, handlerProperties })}
            disabled={false}
            required
          />
        </section>

        <Checkbox
          label="Enable immediately"
          description="Unchecked, the integration is created disabled and can be enabled from its page."
          checked={draft.enable}
          onChange={(e) => update({ enable: e.target.checked })}
        />

        <div className="space-y-3 border-t border-ink-100 pt-4">
          <FormError>{create.error?.message}</FormError>
          <div className="flex justify-end">
            <Button variant="primary" disabled={!valid} busy={create.isPending} onClick={() => create.mutate()}>
              Create integration
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
