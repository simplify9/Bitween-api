import { useEffect } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import { api, type MatchGroup } from "../../api";
import { Button, EmptyState, FormError, LoadingBlock } from "../../components/ui/basics";
import { MatchExpressionEditor } from "../../components/config/MatchExpressionEditor";
import { IntegrationPicker, PartnerPicker } from "../../components/config/pickers";
import { usePersistentDraft } from "../../components/config/wizard";
import { ReturnBanner } from "../../components/ui/ReturnBanner";
import { takePicked, useHereAsReturnTarget, useReturnContext } from "../../lib/returnTo";

interface Draft {
  matchExpression: MatchGroup | null;
  integrationId: number | null;
  /** null = not seeded yet; "none" = explicitly no partner. */
  partner: number | "none" | null;
}

/** Routed edit page for one bus route — reached from the gateway's own page. */
export function EditRoutePage() {
  const { id = "", routeId = "" } = useParams();
  const gatewayId = Number(id);
  const rid = Number(routeId);
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
  const infoType = useQuery({
    queryKey: ["information-type", gateway.data?.informationTypeId],
    queryFn: () => api.getInformationType(gateway.data!.informationTypeId),
    enabled: !!gateway.data,
  });
  const route = gateway.data?.routes.find((r) => r.id === rid);

  const [draft, update, clear] = usePersistentDraft<Draft>(
    `bitween-draft-edit-route-${gatewayId}-${rid}`,
    { matchExpression: null, integrationId: null, partner: null },
  );

  // seed from the current route once it loads (skipped if a detour already picked something)
  useEffect(() => {
    if (draft.integrationId === null && draft.partner === null && route) {
      update({
        matchExpression: structuredClone(route.matchExpression),
        integrationId: route.integrationId,
        partner: route.partnerId ?? "none",
      });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [route]);

  // returning from a "New partner" / "New integration" detour
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

  const save = useMutation({
    mutationFn: () =>
      api.updateBusRoute(gatewayId, rid, {
        integrationId: draft.integrationId!,
        partnerId: draft.partner === "none" ? null : draft.partner,
        matchExpression: draft.matchExpression,
      }),
    onSuccess: () => {
      clear();
      void queryClient.invalidateQueries({ queryKey: ["bus-gateway", gatewayId] });
      void queryClient.invalidateQueries({ queryKey: ["integrations"] });
      navigate(`/bus-gateways/${gatewayId}`);
    },
  });

  if (gateway.isPending) return <LoadingBlock label="Loading gateway…" />;
  if (gateway.isError || !route)
    return (
      <EmptyState title="This route no longer exists">
        <Link to={`/bus-gateways/${gatewayId}`} className="font-medium text-crimson-700 hover:underline">
          Back to the gateway
        </Link>
      </EmptyState>
    );

  const g = gateway.data;
  const detourCtx = { to: here, label: `Editing a route on ${g.name}` };

  return (
    <div className="pb-10">
      <Link
        to={ctx?.to ?? `/bus-gateways/${gatewayId}`}
        className="mb-4 inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800"
      >
        <ArrowLeft className="size-3.5" /> {ctx ? "Back without saving" : g.name}
      </Link>

      <ReturnBanner />

      <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">Edit route</h1>
      <p className="mt-1 text-sm text-ink-500">Runs against every {g.informationTypeCode} message on the bus.</p>

      <div className="mt-6 max-w-2xl space-y-6 rounded-xl border border-ink-200 bg-white p-5">
        <div>
          <h2 className="mb-2 text-[13px] font-medium text-ink-700">When a message matches</h2>
          <MatchExpressionEditor
            value={draft.matchExpression}
            onChange={(matchExpression) => update({ matchExpression })}
            properties={infoType.data?.promotedProperties ?? []}
            disabled={false}
          />
        </div>

        <section>
          <h2 className="mb-2 text-[13px] font-medium text-ink-700">Run the integration</h2>
          <IntegrationPicker
            type="BusGateway"
            informationTypeId={g.informationTypeId}
            value={draft.integrationId}
            onChange={(integrationId) => update({ integrationId })}
            detourCtx={detourCtx}
          />
        </section>

        <section>
          <h2 className="mb-2 text-[13px] font-medium text-ink-700">On behalf of partner</h2>
          <p className="mb-3 text-[13px] text-ink-500">
            {"Supplies {{partner.…}} values to the adapters."}
          </p>
          <PartnerPicker
            value={draft.partner}
            onChange={(partner) => update({ partner })}
            allowNone
            noneLabel="No partner"
            noneSubtitle="The integration runs without {{partner.…}} values."
            detourCtx={detourCtx}
          />
        </section>

        <FormError>{save.error?.message}</FormError>
        <div className="flex justify-end gap-2 border-t border-ink-100 pt-4">
          <Button onClick={() => navigate(`/bus-gateways/${gatewayId}`)}>Cancel</Button>
          <Button
            variant="primary"
            busy={save.isPending}
            disabled={draft.integrationId === null}
            onClick={() => save.mutate()}
          >
            Save route
          </Button>
        </div>
      </div>
    </div>
  );
}
