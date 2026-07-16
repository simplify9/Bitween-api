import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import { api } from "../../api";
import { Button, FormError } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { ReturnBanner } from "../../components/ui/ReturnBanner";
import { useReturnContext, withReturn } from "../../lib/returnTo";
import { suggestSlug } from "../../lib/identifiers";

export function ApiGatewayNewPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const ctx = useReturnContext();
  const [name, setName] = useState("");
  const [urlName, setUrlName] = useState("");
  const [urlTouched, setUrlTouched] = useState(false);

  const create = useMutation({
    mutationFn: () => api.createApiGateway({ name, urlName }),
    onSuccess: (gateway) => {
      void queryClient.invalidateQueries({ queryKey: ["api-gateways"] });
      const base = `/api-gateways/${gateway.id}`;
      navigate(ctx ? `${withReturn(base, ctx)}&picked=api-gateway:${gateway.id}` : base);
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    create.mutate();
  };

  return (
    <div>
      <Link
        to={ctx?.to ?? "/subscriptions?types=api-gateways"}
        className="mb-4 inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800"
      >
        <ArrowLeft className="size-3.5" /> {ctx ? "Back without creating" : "Integrations"}
      </Link>

      <ReturnBanner />

      <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">New API gateway</h1>
      <p className="mt-1 text-sm text-ink-500">
        Partners call its URL with their API key. Attach partners afterwards on its page.
      </p>

      <form onSubmit={submit} className="mt-6 max-w-lg space-y-4 rounded-xl border border-ink-200 bg-white p-5">
        <Field label="Name" htmlFor="nag-name">
          <TextInput
            id="nag-name"
            required
            autoFocus
            value={name}
            onChange={(e) => {
              setName(e.target.value);
              if (!urlTouched) setUrlName(suggestSlug(e.target.value));
            }}
            placeholder="e.g. Orders inbound"
          />
        </Field>
        <Field
          label="URL name"
          htmlFor="nag-url"
          hint={`Partners will call /api/Gateway/${urlName || "…"}/sync or /async.`}
        >
          <TextInput
            id="nag-url"
            required
            value={urlName}
            className="font-mono"
            onChange={(e) => {
              setUrlTouched(true);
              setUrlName(e.target.value.toLowerCase());
            }}
            placeholder="orders"
          />
        </Field>
        <FormError>{create.error?.message}</FormError>
        <div className="flex justify-end">
          <Button type="submit" variant="primary" busy={create.isPending}>
            Create gateway
          </Button>
        </div>
      </form>
    </div>
  );
}
