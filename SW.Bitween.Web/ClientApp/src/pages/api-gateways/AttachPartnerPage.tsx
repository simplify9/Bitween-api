import { useEffect } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import { api } from "../../api";
import { Button, EmptyState, FormError, LoadingBlock } from "../../components/ui/basics";
import { Field } from "../../components/ui/forms";
import { IntegrationPicker, PartnerPicker } from "../../components/config/pickers";
import { ReturnBanner } from "../../components/ui/ReturnBanner";
import { usePersistentDraft } from "../../lib/persistentDraft";
import { takePicked, useHereAsReturnTarget, useReturnContext } from "../../lib/returnTo";

interface Draft {
  partnerId: number | null;
  integrationId: number | null;
}

/**
 * Routed create page for one gateway attachment — who calls, and what runs when
 * they do. Deliberately shaped like `EditAttachmentPage`, its edit twin: two
 * questions on one form, not a guided flow.
 */
export function AttachPartnerPage() {
  const { id = "" } = useParams();
  const gatewayId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const ctx = useReturnContext();
  const here = useHereAsReturnTarget();
  const [params, setParams] = useSearchParams();

  const gateway = useQuery({
    queryKey: ["api-gateway", gatewayId],
    queryFn: () => api.getApiGateway(gatewayId),
    retry: false,
  });
  const partners = useQuery({ queryKey: ["partners"], queryFn: () => api.listPartners() });

  const [draft, update, clear] = usePersistentDraft<Draft>(`bitween-draft-attach-${gatewayId}`, {
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
      api.attachGatewayPartner(gatewayId, {
        partnerId: draft.partnerId!,
        integrationId: draft.integrationId!,
      }),
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
        <Link to="/api-gateways" className="font-medium text-crimson-700 hover:underline">
          Back to API gateways
        </Link>
      </EmptyState>
    );

  const g = gateway.data;
  const detourCtx = { to: here, label: `Attaching a partner to ${g.name}` };
  const valid = draft.partnerId !== null && draft.integrationId !== null;
  // Named so a "New integration" detour can say what it will be wired into.
  // Cached query — PartnerPicker is already reading the same key.
  const partnerName = partners.data?.find((p) => p.id === draft.partnerId)?.name ?? "";

  return (
    <div className="pb-10">
      <Link
        to={ctx?.to ?? `/api-gateways/${gatewayId}`}
        className="mb-4 inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800"
      >
        <ArrowLeft className="size-3.5" /> {ctx ? "Back without attaching" : g.name}
      </Link>

      <ReturnBanner />

      <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">
        Attach a partner to {g.name}
      </h1>
      <p className="mt-1 text-sm text-ink-500">
        Anything missing is created on its own page — you continue right back here.
      </p>

      <div className="mt-6 max-w-2xl space-y-5 rounded-xl border border-ink-200 bg-white p-5">
        <Field
          label="Partner"
          htmlFor="ap-partner"
          hint={`Who calls /api/Gateway/${g.urlName}. Partners already attached aren't listed.`}
        >
          <PartnerPicker
            id="ap-partner"
            value={draft.partnerId}
            onChange={(v) => update({ partnerId: v === "none" ? null : v })}
            excludeIds={g.attachments.map((a) => a.partnerId)}
            detourCtx={detourCtx}
          />
        </Field>

        <Field label="Integration" htmlFor="ap-integration" hint="What runs when they call.">
          <IntegrationPicker
            id="ap-integration"
            type="GatewayApiCall"
            value={draft.integrationId}
            onChange={(integrationId) => update({ integrationId })}
            detourCtx={detourCtx}
            triggerHint={partnerName ? `${g.name} → ${partnerName}` : g.name}
          />
        </Field>

        <FormError>{create.error?.message}</FormError>
        <div className="flex justify-end gap-2 border-t border-ink-100 pt-4">
          <Button onClick={() => navigate(`/api-gateways/${gatewayId}`)}>Cancel</Button>
          <Button
            variant="primary"
            busy={create.isPending}
            disabled={!valid}
            onClick={() => create.mutate()}
          >
            Attach partner
          </Button>
        </div>
      </div>
    </div>
  );
}
