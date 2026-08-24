import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api";
import { Button, EmptyState, FormError, LoadingBlock } from "../../components/ui/basics";
import { IntegrationPicker } from "../../components/config/pickers";
import { BackLink } from "../../components/ui/BackLink";

/** Local draft state with the patch-and-clear shape the form bodies already use. */
function useDraft<T extends object>(initial: T) {
  const [draft, setDraft] = useState<T>(initial);
  const update = (patch: Partial<T>) => setDraft((d) => ({ ...d, ...patch }));
  const clear = () => setDraft(initial);
  return [draft, update, clear] as const;
}


interface Draft {
  integrationId: number | null;
}

/** Routed edit page for one gateway attachment — reached from the gateway's own page. */
export function EditAttachmentPage() {
  const { id = "", partnerId = "" } = useParams();
  const gatewayId = Number(id);
  const pid = Number(partnerId);
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const gateway = useQuery({
    queryKey: ["api-gateway", gatewayId],
    queryFn: () => api.getApiGateway(gatewayId),
    retry: false,
  });
  const attachment = gateway.data?.attachments.find((a) => a.partnerId === pid);

  const [draft, update, clear] = useDraft<Draft>({ integrationId: null },
  );

  // seed from the current attachment once it loads (skipped if a detour already picked something)
  useEffect(() => {
    if (draft.integrationId === null && attachment) {
      update({ integrationId: attachment.integrationId });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [attachment]);


  const save = useMutation({
    mutationFn: () => api.updateGatewayAttachment(gatewayId, { partnerId: pid, integrationId: draft.integrationId! }),
    onSuccess: () => {
      clear();
      void queryClient.invalidateQueries({ queryKey: ["api-gateway", gatewayId] });
      void queryClient.invalidateQueries({ queryKey: ["api-gateway-attachments-search"] });
      void queryClient.invalidateQueries({ queryKey: ["integrations"] });
      navigate(`/api-gateways/${gatewayId}`);
    },
  });

  if (gateway.isPending) return <LoadingBlock label="Loading gateway…" />;
  if (gateway.isError || !attachment)
    return (
      <EmptyState title="This attachment no longer exists">
        <Link to={`/api-gateways/${gatewayId}`} className="font-medium text-crimson-700 hover:underline">
          Back to the gateway
        </Link>
      </EmptyState>
    );

  const g = gateway.data;

  return (
    <div className="pb-10">
      <BackLink to={`/api-gateways/${gatewayId}`} label={g.name} />

      <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">
        Integration for {attachment.partnerName}
      </h1>
      <p className="mt-1 text-sm text-ink-500">
        What runs when {attachment.partnerName} calls {g.name}.
      </p>

      <div className="mt-6 max-w-2xl space-y-4 rounded-xl border border-ink-200 bg-white p-5">
        <IntegrationPicker
          type="GatewayApiCall"
          value={draft.integrationId}
          onChange={(integrationId) => update({ integrationId })}
        />
        <FormError>{save.error?.message}</FormError>
        <div className="flex justify-end gap-2 border-t border-ink-100 pt-4">
          <Button onClick={() => navigate(`/api-gateways/${gatewayId}`)}>Cancel</Button>
          <Button
            variant="primary"
            busy={save.isPending}
            disabled={draft.integrationId === null}
            onClick={() => save.mutate()}
          >
            Save
          </Button>
        </div>
      </div>
    </div>
  );
}
