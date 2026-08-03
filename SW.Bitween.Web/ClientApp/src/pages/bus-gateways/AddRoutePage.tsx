import { useEffect } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import { api, type MatchGroup } from "../../api";
import { Button, EmptyState, FormError, LoadingBlock } from "../../components/ui/basics";
import { Field } from "../../components/ui/forms";
import { MatchExpressionEditor } from "../../components/config/MatchExpressionEditor";
import { IntegrationPicker, PartnerPicker } from "../../components/config/pickers";
import { ReturnBanner } from "../../components/ui/ReturnBanner";
import { usePersistentDraft } from "../../lib/persistentDraft";
import { takePicked, useHereAsReturnTarget, useReturnContext } from "../../lib/returnTo";

interface Draft {
  matchExpression: MatchGroup | null;
  /** null = not chosen yet; "none" = explicitly no partner. */
  partner: number | "none" | null;
  integrationId: number | null;
}

/**
 * Routed create page for one bus-gateway route. Shaped like `EditRoutePage`,
 * its edit twin — the same three questions, on one form.
 */
export function AddRoutePage() {
  const { id = "" } = useParams();
  const gatewayId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const ctx = useReturnContext();
  const here = useHereAsReturnTarget();
  const [params, setParams] = useSearchParams();

  const gateway = useQuery({
    queryKey: ["bus-gateway", gatewayId],
    queryFn: () => api.getBusGateway(gatewayId),
    retry: false,
  });
  const partners = useQuery({ queryKey: ["partners"], queryFn: () => api.listPartners() });
  const infoType = useQuery({
    queryKey: ["information-type", gateway.data?.informationTypeId],
    queryFn: () => api.getInformationType(gateway.data!.informationTypeId),
    enabled: !!gateway.data,
  });

  const [draft, update, clear] = usePersistentDraft<Draft>(`bitween-draft-route-${gatewayId}`, {
    matchExpression: null,
    partner: null,
    integrationId: null,
  });

  useEffect(() => {
    const partner = takePicked(params, "partner");
    const integration = takePicked(params, "integration");
    if (partner !== null) update({ partner });
    if (integration !== null) update({ integrationId: integration });
    if (partner !== null || integration !== null) {
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
      api.addBusRoute(gatewayId, {
        integrationId: draft.integrationId!,
        partnerId: draft.partner === "none" ? null : draft.partner,
        matchExpression: draft.matchExpression,
      }),
    onSuccess: () => {
      clear();
      void queryClient.invalidateQueries();
      navigate(`/bus-gateways/${gatewayId}`);
    },
  });

  if (gateway.isPending) return <LoadingBlock label="Loading gateway…" />;
  if (gateway.isError)
    return (
      <EmptyState title="This bus gateway no longer exists">
        <Link to="/bus-gateways" className="font-medium text-crimson-700 hover:underline">
          Back to bus gateways
        </Link>
      </EmptyState>
    );

  const g = gateway.data;
  const detourCtx = { to: here, label: `Adding a route to ${g.name}` };
  const valid = draft.partner !== null && draft.integrationId !== null;
  // Named so a "New integration" detour can say what it will be wired into.
  const partnerName =
    draft.partner === "none"
      ? "no partner"
      : (partners.data?.find((x) => x.id === draft.partner)?.name ?? "");
  const triggerHint = partnerName ? `${g.name} → ${partnerName}` : g.name;

  return (
    <div className="pb-10">
      <Link
        to={ctx?.to ?? `/bus-gateways/${gatewayId}`}
        className="mb-4 inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800"
      >
        <ArrowLeft className="size-3.5" /> {ctx ? "Back without adding" : g.name}
      </Link>

      <ReturnBanner />

      <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">Add a route to {g.name}</h1>
      <p className="mt-1 text-sm text-ink-500">
        Every {g.informationTypeCode} message on the bus is checked against this route.
      </p>

      <div className="mt-6 max-w-3xl space-y-5 rounded-xl border border-ink-200 bg-white p-5">
        <Field label="Filter" hint="Which messages this route picks up. No filter matches every one of them.">
          <MatchExpressionEditor
            value={draft.matchExpression}
            onChange={(matchExpression) => update({ matchExpression })}
            properties={infoType.data?.promotedProperties ?? []}
            disabled={false}
          />
        </Field>

        <Field
          label="Partner"
          htmlFor="ar-partner"
          hint="Whose {{partner.…}} values the integration resolves against."
        >
          <PartnerPicker
            id="ar-partner"
            value={draft.partner}
            onChange={(partner) => update({ partner })}
            allowNone
            noneLabel="No partner"
            noneSubtitle="Runs without {{partner.…}} values"
            detourCtx={detourCtx}
          />
        </Field>

        <Field label="Integration" htmlFor="ar-integration" hint="What runs for matched messages.">
          <IntegrationPicker
            id="ar-integration"
            type="BusGateway"
            informationTypeId={g.informationTypeId}
            value={draft.integrationId}
            onChange={(integrationId) => update({ integrationId })}
            detourCtx={detourCtx}
            triggerHint={triggerHint}
          />
        </Field>

        <FormError>{create.error?.message}</FormError>
        <div className="flex justify-end gap-2 border-t border-ink-100 pt-4">
          <Button onClick={() => navigate(`/bus-gateways/${gatewayId}`)}>Cancel</Button>
          <Button
            variant="primary"
            busy={create.isPending}
            disabled={!valid}
            onClick={() => create.mutate()}
          >
            Add route
          </Button>
        </div>
      </div>
    </div>
  );
}
