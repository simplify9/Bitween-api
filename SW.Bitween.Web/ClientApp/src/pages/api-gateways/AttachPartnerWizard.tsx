import { useEffect } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api";
import { EmptyState, LoadingBlock } from "../../components/ui/basics";
import { IntegrationPicker, PartnerPicker } from "../../components/config/pickers";
import { StepNav, WizardShell, usePersistentDraft } from "../../components/config/wizard";
import { useIntegrationsCache } from "../../components/config/shared";
import { takePicked, useHereAsReturnTarget } from "../../lib/returnTo";

type Stage = "partner" | "integration" | "review";
const STEPS = ["Partner", "Integration", "Review"];

interface Draft {
  stage: Stage;
  partnerId: number | null;
  integrationId: number | null;
}

export function AttachPartnerWizard() {
  const { id = "" } = useParams();
  const gatewayId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const here = useHereAsReturnTarget();
  const [params, setParams] = useSearchParams();

  const gateway = useQuery({
    queryKey: ["api-gateway", gatewayId],
    queryFn: () => api.getApiGateway(gatewayId),
    retry: false,
  });
  const integrations = useIntegrationsCache();
  const infoTypes = useQuery({ queryKey: ["information-types"], queryFn: () => api.listInformationTypes() });
  const partners = useQuery({ queryKey: ["partners"], queryFn: () => api.listPartners() });

  const [draft, update, clear] = usePersistentDraft<Draft>(`bitween-draft-attach-${gatewayId}`, {
    stage: "partner",
    partnerId: null,
    integrationId: null,
  });

  // returning from a create detour: apply the pick and consume the param
  useEffect(() => {
    const partner = takePicked(params, "partner");
    const integration = takePicked(params, "integration");
    if (partner !== null) update({ partnerId: partner });
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
      api.attachGatewayPartner(gatewayId, { partnerId: draft.partnerId!, integrationId: draft.integrationId! }),
    onSuccess: () => {
      clear();
      void queryClient.invalidateQueries();
      navigate(`/api-gateways/${gatewayId}`);
    },
  });

  if (gateway.isPending) return <LoadingBlock label="Loading gateway…" />;
  if (gateway.isError)
    return (
      <EmptyState title="This API gateway no longer exists">
        <Link to="/subscriptions?types=api-gateways" className="font-medium text-crimson-700 hover:underline">
          Back to integrations
        </Link>
      </EmptyState>
    );

  const g = gateway.data;
  const detourCtx = { to: here, label: `Attaching a partner to ${g.name}` };
  const gatewayIntegrations = (integrations.data ?? []).filter((s) => s.type === "GatewayApiCall");
  const current = STEPS.indexOf(draft.stage === "partner" ? "Partner" : draft.stage === "integration" ? "Integration" : "Review");

  const partnerName = partners.data?.find((p) => p.id === draft.partnerId)?.name ?? "…";
  const integrationName = gatewayIntegrations.find((s) => s.id === draft.integrationId)?.name ?? "…";
  const infoTypeCode =
    infoTypes.data?.find(
      (t) => t.id === gatewayIntegrations.find((s) => s.id === draft.integrationId)?.informationTypeId,
    )?.code ?? "…";

  return (
    <WizardShell
      title={`Attach a partner to ${g.name}`}
      subtitle="Pick who calls and what runs — anything missing is created on its own page and you continue here."
      backTo={`/api-gateways/${gatewayId}`}
      backLabel={g.name}
      steps={STEPS}
      current={current}
    >
      {draft.stage === "partner" && (
        <>
          <h2 className="mb-3 text-[15px] font-semibold text-ink-900">Who is calling this gateway?</h2>
          <PartnerPicker
            value={draft.partnerId}
            onChange={(v) => update({ partnerId: v === "none" ? null : v })}
            excludeIds={g.attachments.map((a) => a.partnerId)}
            detourCtx={detourCtx}
          />
          <StepNav onNext={() => update({ stage: "integration" })} nextDisabled={draft.partnerId === null} />
        </>
      )}

      {draft.stage === "integration" && (
        <>
          <h2 className="mb-3 text-[15px] font-semibold text-ink-900">What should run when they call?</h2>
          <IntegrationPicker
            type="GatewayApiCall"
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
            <ReviewRow label="Gateway" value={`${g.name} — /api/Gateway/${g.urlName}`} />
            <ReviewRow label="Partner" value={partnerName} />
            <ReviewRow label="Integration" value={integrationName} />
            <ReviewRow label="Carries" value={infoTypeCode} />
          </dl>
          <StepNav
            onBack={() => update({ stage: "integration" })}
            onNext={() => create.mutate()}
            nextLabel="Attach partner"
            busy={create.isPending}
            error={create.error?.message}
          />
        </>
      )}
    </WizardShell>
  );
}

export function ReviewRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-start gap-3">
      <dt className="w-28 shrink-0 text-ink-500">{label}</dt>
      <dd className="min-w-0 flex-1 font-medium wrap-break-word text-ink-800">{value}</dd>
    </div>
  );
}
