import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api";
import { Button, FormError } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { finishUrlName, suggestSlug, toUrlName, urlNameProblem } from "../../lib/identifiers";
import { BackLink } from "../../components/ui/BackLink";
import { StillNeeds, useFormCheck } from "../../components/ui/formCheck";
import { keys } from "../../api/queryKeys";
import { splitErrors } from "../../lib/fieldErrors";
import { GATEWAY_ERRORS } from "./gatewayErrors";

export function ApiGatewayNewPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [urlName, setUrlName] = useState("");
  const [urlTouched, setUrlTouched] = useState(false);

  const create = useMutation({
    mutationFn: () => api.createApiGateway({ name, urlName: finishUrlName(urlName) }),
    onSuccess: (gateway) => {
      void queryClient.invalidateQueries({ queryKey: keys.apiGateways.all });
      const base = `/api-gateways/${gateway.id}`;
      navigate(base, { replace: true });
    },
  });

  // Empty is left to the check on Create, or the page would open on an error.
  const urlProblem = urlName ? urlNameProblem(urlName) : null;
  const refused = splitErrors(create.error, GATEWAY_ERRORS);

  const check = useFormCheck([
    {
      field: "name",
      failing: !name.trim(),
      needs: "a name",
      message: "Give the gateway a name, e.g. Orders inbound.",
      focus: "nag-name",
    },
    {
      field: "url",
      failing: !urlName,
      needs: "a URL name",
      message: "Give it the URL name partners will call, e.g. orders.",
      focus: "nag-url",
    },
    // Already said under the field as it is typed, so the message adds nothing.
    { field: "url", failing: !!urlProblem, needs: "a valid URL name", message: urlProblem ?? "", focus: "nag-url" },
  ]);

  const submit = (e: FormEvent) => {
    e.preventDefault();
    check.submit(() => create.mutate());
  };

  return (
    <div>
      <BackLink to="/api-gateways" label="API gateways" />

      <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">New API gateway</h1>
      <p className="mt-1 text-sm text-ink-500">
        Partners call its URL with their API key. Attach partners afterwards on its page.
      </p>

      <form noValidate onSubmit={submit} className="mt-6 max-w-lg space-y-4 rounded-xl border border-ink-200 bg-white p-5">
        <Field label="Name" htmlFor="nag-name" error={check.errors.name ?? refused.byField.name}>
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
          error={urlProblem ?? check.errors.url ?? refused.byField.url}
          hint={`Partners will call /api/gateway/${urlName || "…"}/sync or /async. Use / to split it into parts, e.g. logistics/slim/orders.`}
        >
          <TextInput
            id="nag-url"
            required
            value={urlName}
            className="font-mono"
            onChange={(e) => {
              setUrlTouched(true);
              setUrlName(toUrlName(e.target.value));
            }}
            placeholder="orders"
          />
        </Field>
        <FormError>{refused.rest}</FormError>
        <div className="flex items-center justify-end gap-3">
          <StillNeeds missing={check.missing} />
          <Button type="submit" variant="primary" busy={create.isPending}>
            Create gateway
          </Button>
        </div>
      </form>
    </div>
  );
}
