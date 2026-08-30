import { useState } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { useMutation, useQuery } from "@tanstack/react-query";
import { api, ApiRequestError } from "../../api";
import { PageHeader } from "../../components/layout/PageHeader";
import { Button, FormError } from "../../components/ui/basics";
import { Field } from "../../components/ui/forms";
import { SearchSelect } from "../../components/ui/SearchSelect";
import { useSubscriptionsCache } from "../../components/config/shared";
import { keys } from "../../api/queryKeys";

/**
 * Manually inject a payload — useful for testing a pipeline without waiting
 * for real traffic. Addressed either at one subscription, or at an
 * information type (every matching subscription picks it up).
 */
export function ExchangeNewPage() {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const target = searchParams.get("target") === "informationType" ? "informationType" : "subscription";
  const setTarget = (t: "subscription" | "informationType") => {
    const next = new URLSearchParams(searchParams);
    next.set("target", t);
    setSearchParams(next, { replace: true });
  };
  const [subscriptionId, setSubscriptionId] = useState("");
  const [informationTypeId, setInformationTypeId] = useState("");
  const [data, setData] = useState("");
  const [error, setError] = useState<string | null>(null);

  const subscriptions = useSubscriptionsCache().data ?? [];
  const infoTypes =
    useQuery({ queryKey: keys.informationTypes.list, queryFn: () => api.listInformationTypes() }).data ?? [];

  const create = useMutation({
    mutationFn: () =>
      api.createExchange({
        target,
        subscriptionId: subscriptionId ? Number(subscriptionId) : undefined,
        informationTypeId: informationTypeId ? Number(informationTypeId) : undefined,
        data,
      }),
    onSuccess: ({ id }) => navigate(`/exchanges?ids=${encodeURIComponent(id)}`),
    onError: (e) =>
      setError(e instanceof ApiRequestError ? e.message : "The exchange could not be created."),
  });

  const submit = () => {
    setError(null);
    if (target === "subscription" && !subscriptionId) return setError("Pick the subscription to run.");
    if (target === "informationType" && !informationTypeId)
      return setError("Pick the information type to send.");
    if (!data.trim()) return setError("Paste the payload the exchange should carry.");
    create.mutate();
  };

  return (
    <div className="mx-auto max-w-2xl">
      <PageHeader
        title="New exchange"
        description="Inject a payload by hand — it runs through the pipeline exactly like real traffic."
      />

      <div className="space-y-5 rounded-xl border border-ink-200 bg-white p-5">
        <div className="flex gap-2">
          {(
            [
              { id: "subscription", label: "Send to a subscription" },
              { id: "informationType", label: "Send as an information type" },
            ] as const
          ).map((t) => (
            <button
              key={t.id}
              onClick={() => setTarget(t.id)}
              aria-pressed={target === t.id}
              className={`rounded-full px-3 py-1.5 text-[13px] font-medium transition-colors ${
                target === t.id
                  ? "bg-ink-900 text-white"
                  : "border border-ink-200 bg-white text-ink-600 hover:border-ink-300 hover:bg-ink-50"
              }`}
            >
              {t.label}
            </button>
          ))}
        </div>

        {target === "subscription" ? (
          <Field
            label="Subscription"
            hint="The pipeline that will process this payload."
          >
            <SearchSelect
              value={subscriptionId}
              onChange={setSubscriptionId}
              placeholder="Pick a subscription…"
              options={subscriptions.map((i) => ({ value: String(i.id), label: i.name }))}
            />
          </Field>
        ) : (
          <Field
            label="Information type"
            hint="Every subscription listening for this type picks the payload up."
          >
            <SearchSelect
              value={informationTypeId}
              onChange={setInformationTypeId}
              placeholder="Pick an information type…"
              options={infoTypes.map((t) => ({ value: String(t.id), label: t.name, code: t.code }))}
            />
          </Field>
        )}

        <Field label="Payload" hint="JSON or XML — whatever the pipeline expects as its input document.">
          <textarea
            value={data}
            onChange={(e) => setData(e.target.value)}
            rows={10}
            spellCheck={false}
            placeholder='{"order": { … }}'
            className="w-full resize-y rounded-lg border border-ink-200 bg-white px-3 py-2 font-mono text-xs text-ink-900 placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
          />
        </Field>

        <FormError>{error}</FormError>

        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={() => navigate("/exchanges")}>
            Cancel
          </Button>
          <Button variant="primary" busy={create.isPending} onClick={submit}>
            Create exchange
          </Button>
        </div>
      </div>
    </div>
  );
}
