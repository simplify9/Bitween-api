import { useEffect } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type MatchGroup } from "../../api";
import { EmptyState, LoadingBlock } from "../../components/ui/basics";
import { MatchExpressionEditor } from "../../components/config/MatchExpressionEditor";
import { IntegrationPicker, PartnerPicker } from "../../components/config/pickers";
import { StepNav, WizardShell, usePersistentDraft } from "../../components/config/wizard";
import { useIntegrationsCache } from "../../components/config/shared";
import { takePicked, useHereAsReturnTarget } from "../../lib/returnTo";
import { ReviewRow } from "../api-gateways/AttachPartnerWizard";
import { matchSummary } from "../../lib/match";

type Stage = "filter" | "partner" | "integration" | "review";
const STEPS = ["Filter", "Partner", "Integration", "Review"];

interface Draft {
  stage: Stage;
  matchExpression: MatchGroup | null;
  /** null = not chosen yet; "none" = explicitly no partner. */
  partner: number | "none" | null;
  integrationId: number | null;
}

export function AddRouteWizard() {
  const { id = "" } = useParams();
  const gatewayId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const here = useHereAsReturnTarget();
  const [params, setParams] = useSearchParams();

  const gateway = useQuery({
    queryKey: ["bus-gateway", gatewayId],
    queryFn: () => api.getBusGateway(gatewayId),
    retry: false,
  });
  const infoType = useQuery({
    queryKey: ["information-type", gateway.data?.informationTypeId],
    queryFn: () => api.getInformationType(gateway.data!.informationTypeId),
    enabled: !!gateway.data,
  });
  const integrations = useIntegrationsCache();
  const partners = useQuery({ queryKey: ["partners"], queryFn: () => api.listPartners() });

  const [draft, update, clear] = usePersistentDraft<Draft>(`bitween-draft-route-${gatewayId}`, {
    stage: "filter",
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
        <Link to="/subscriptions?types=bus-gateways" className="font-medium text-crimson-700 hover:underline">
          Back to integrations
        </Link>
      </EmptyState>
    );

  const g = gateway.data;
  const detourCtx = { to: here, label: `Adding a route to ${g.name}` };
  const candidates = (integrations.data ?? []).filter(
    (s) => s.type === "BusGateway" && s.informationTypeId === g.informationTypeId,
  );
  const current = STEPS.indexOf(
    draft.stage === "filter" ? "Filter" : draft.stage === "partner" ? "Partner" : draft.stage === "integration" ? "Integration" : "Review",
  );

  const partnerLabel =
    draft.partner === "none"
      ? "No partner"
      : (partners.data?.find((p) => p.id === draft.partner)?.name ?? "…");

  return (
    <WizardShell
      title={`Add a route to ${g.name}`}
      subtitle={`Every ${g.informationTypeCode} message on the bus is checked against this route.`}
      backTo={`/bus-gateways/${gatewayId}`}
      backLabel={g.name}
      steps={STEPS}
      current={current}
    >
      {draft.stage === "filter" && (
        <>
          <h2 className="mb-3 text-[15px] font-semibold text-ink-900">Which messages should this route pick up?</h2>
          <MatchExpressionEditor
            value={draft.matchExpression}
            onChange={(matchExpression) => update({ matchExpression })}
            properties={infoType.data?.promotedProperties ?? []}
            disabled={false}
          />
          <StepNav onNext={() => update({ stage: "partner" })} />
        </>
      )}

      {draft.stage === "partner" && (
        <>
          <h2 className="mb-3 text-[15px] font-semibold text-ink-900">Does this route run for a partner?</h2>
          <PartnerPicker
            value={draft.partner}
            onChange={(partner) => update({ partner })}
            allowNone
            noneLabel="No partner"
            noneSubtitle="The integration runs without {{partner.…}} values."
            detourCtx={detourCtx}
          />
          <StepNav
            onBack={() => update({ stage: "filter" })}
            onNext={() => update({ stage: "integration" })}
            nextDisabled={draft.partner === null}
          />
        </>
      )}

      {draft.stage === "integration" && (
        <>
          <h2 className="mb-3 text-[15px] font-semibold text-ink-900">What should run for matched messages?</h2>
          <IntegrationPicker
            type="BusGateway"
            informationTypeId={g.informationTypeId}
            value={draft.integrationId}
            onChange={(integrationId) => update({ integrationId })}
            detourCtx={detourCtx}
          />
          <StepNav
            onBack={() => update({ stage: "partner" })}
            onNext={() => update({ stage: "review" })}
            nextDisabled={draft.integrationId === null}
          />
        </>
      )}

      {draft.stage === "review" && (
        <>
          <h2 className="mb-3 text-[15px] font-semibold text-ink-900">Review</h2>
          <dl className="space-y-2.5 text-sm">
            <ReviewRow label="Gateway" value={`${g.name} — listens for ${g.informationTypeCode}`} />
            <ReviewRow label="Filter" value={matchSummary(draft.matchExpression)} />
            <ReviewRow label="Partner" value={partnerLabel} />
            <ReviewRow
              label="Integration"
              value={candidates.find((s) => s.id === draft.integrationId)?.name ?? "…"}
            />
          </dl>
          <StepNav
            onBack={() => update({ stage: "integration" })}
            onNext={() => create.mutate()}
            nextLabel="Add route"
            busy={create.isPending}
            error={create.error?.message}
          />
        </>
      )}
    </WizardShell>
  );
}
