import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api";
import { Button, EmptyState, FormError, LoadingBlock } from "../../components/ui/basics";
import { Field } from "../../components/ui/forms";
import { IntegrationPicker, PartnerPicker } from "../../components/config/pickers";
import { BackLink } from "../../components/ui/BackLink";

/** Local draft state with the patch-and-clear shape the form bodies already use. */
function useDraft<T extends object>(initial: T) {
  const [draft, setDraft] = useState<T>(initial);
  const update = (patch: Partial<T>) => setDraft((d) => ({ ...d, ...patch }));
  const clear = () => setDraft(initial);
  return [draft, update, clear] as const;
}


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

  const gateway = useQuery({
    queryKey: ["api-gateway", gatewayId],
    queryFn: () => api.getApiGateway(gatewayId),
    retry: false,
  });

  const [draft, update, clear] = useDraft<Draft>({
    partnerId: null,
    integrationId: null,
  });


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
  const valid = draft.partnerId !== null && draft.integrationId !== null;

  return (
    <div className="pb-10">
      <BackLink to={`/api-gateways/${gatewayId}`} label={g.name} />

      <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">
        Attach a partner to {g.name}
      </h1>
      <p className="mt-1 text-sm text-ink-500">
        Anything you don't have yet — the partner, the integration — is created right here.
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
          />
        </Field>

        <Field label="Integration" htmlFor="ap-integration" hint="What runs when they call.">
          <IntegrationPicker
            id="ap-integration"
            type="GatewayApiCall"
            value={draft.integrationId}
            onChange={(integrationId) => update({ integrationId })}
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
