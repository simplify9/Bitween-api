import { useEffect, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api";
import { Button, EmptyState, FormError, LoadError, LoadingBlock } from "../../components/ui/basics";
import { isNotFound } from "../../components/ui/isNotFound";
import { Field } from "../../components/ui/forms";
import { SubscriptionPicker, PartnerPicker } from "../../components/config/pickers";
import { BackLink } from "../../components/ui/BackLink";
import { StillNeeds, useFormCheck } from "../../components/ui/formCheck";
import { keys } from "../../api/queryKeys";
import { useCrumbTitle } from "../../lib/pageTitle";

/** Local draft state with the patch-and-clear shape the form bodies already use. */
function useDraft<T extends object>(initial: T) {
  const [draft, setDraft] = useState<T>(initial);
  const update = (patch: Partial<T>) => setDraft((d) => ({ ...d, ...patch }));
  const clear = () => setDraft(initial);
  return [draft, update, clear] as const;
}

interface Draft {
  partnerId: number | null;
  subscriptionId: number | null;
}

/**
 * Routed create page for one gateway attachment - who calls, and what runs when
 * they do. Deliberately shaped like `EditAttachmentPage`, its edit twin: two
 * questions on one form, not a guided flow.
 *
 * "New subscription" leaves this page rather than opening in place - see
 * `NewGatewaySubscriptionPage` - so `?picked=`/`?partnerId=` restore the choices
 * this page had on the way out.
 */
export function AttachPartnerPage() {
  const { id = "" } = useParams();
  const gatewayId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();

  const gateway = useQuery({
    queryKey: keys.apiGateways.detail(gatewayId),
    queryFn: () => api.getApiGateway(gatewayId),
    retry: false,
  });
  useCrumbTitle("apiGateway", gateway.data?.name);

  const [draft, update, clear] = useDraft<Draft>({
    partnerId: searchParams.get("partnerId") ? Number(searchParams.get("partnerId")) : null,
    subscriptionId: searchParams.get("picked") ? Number(searchParams.get("picked")) : null,
  });

  // Consumed once, on the way back from creating a subscription - cleared so a
  // refresh of this page doesn't keep re-seeding the same values.
  useEffect(() => {
    if (searchParams.has("picked") || searchParams.has("partnerId")) setSearchParams({}, { replace: true });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const create = useMutation({
    mutationFn: () =>
      api.attachGatewayPartner(gatewayId, {
        partnerId: draft.partnerId!,
        subscriptionId: draft.subscriptionId!,
      }),
    onSuccess: () => {
      clear();
      void queryClient.invalidateQueries();
      navigate(`/api-gateways/${gatewayId}`, { replace: true });
    },
  });

  // The same rule the server enforces, said before the button is pressed rather than
  // after: a subscription cannot be attached half-made.
  const check = useFormCheck([
    {
      field: "partner",
      failing: draft.partnerId === null,
      needs: "a partner",
      message: "Pick the partner who calls this gateway.",
      focus: "ap-partner",
    },
    {
      field: "subscription",
      failing: draft.subscriptionId === null,
      needs: "a subscription",
      message: "Pick what runs when they call, or define a new subscription.",
      focus: "ap-subscription",
    },
  ]);

  if (gateway.isPending) return <LoadingBlock label="Loading gateway…" />;
  if (gateway.isError && !isNotFound(gateway.error))
    return <LoadError error={gateway.error} what="this API gateway" onRetry={() => void gateway.refetch()} />;
  if (gateway.isError)
    return (
      <EmptyState title="This API gateway no longer exists">
        <Link to="/api-gateways" className="font-medium link">
          Back to API gateways
        </Link>
      </EmptyState>
    );

  const g = gateway.data;

  return (
    <div className="pb-10">
      <BackLink to={`/api-gateways/${gatewayId}`} label={g.name} />

      <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">
        Attach a partner to {g.name}
      </h1>
      <p className="mt-1 text-sm text-ink-500">
        Who calls in, and what runs when they do. No partner yet, create one right here; no
        subscription yet, its own page opens next and brings you back.
      </p>

      <div className="mt-6 max-w-2xl space-y-5 rounded-xl border border-ink-200 bg-white p-5">
        <Field
          label="Partner"
          htmlFor="ap-partner"
          hint={`Who calls /api/gateway/${g.urlName}. Partners already attached aren't listed.`}
          error={check.errors.partner}
        >
          <PartnerPicker
            id="ap-partner"
            value={draft.partnerId}
            onChange={(v) => update({ partnerId: v === "none" ? null : v })}
            excludeIds={g.attachments.map((a) => a.partnerId)}
          />
        </Field>

        <Field label="Subscription" htmlFor="ap-subscription" hint="What runs when they call." error={check.errors.subscription}>
          <SubscriptionPicker
            id="ap-subscription"
            type="GatewayApiCall"
            value={draft.subscriptionId}
            onChange={(subscriptionId) => update({ subscriptionId })}
            // Replaced rather than pushed: the detour comes straight back here with
            // `?picked=`, so the whole round trip is one step and leaves behind one
            // history entry instead of two spent forms.
            onDefineHere={() =>
              navigate(
                `/api-gateways/${gatewayId}/attach/new-subscription${
                  draft.partnerId ? `?partnerId=${draft.partnerId}` : ""
                }`,
                { replace: true },
              )
            }
          />
        </Field>

        <FormError>{create.error?.message}</FormError>
        <div className="flex items-center justify-end gap-3 border-t border-ink-100 pt-4">
          <StillNeeds missing={check.missing} />
          <Button onClick={() => navigate(`/api-gateways/${gatewayId}`, { replace: true })}>Cancel</Button>
          <Button variant="primary" busy={create.isPending} onClick={() => check.submit(() => create.mutate())}>
            Attach partner
          </Button>
        </div>
      </div>
    </div>
  );
}
