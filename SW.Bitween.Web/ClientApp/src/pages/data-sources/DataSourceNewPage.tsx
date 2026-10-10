import { useState } from "react";
import { useNavigate } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api, ApiRequestError } from "../../api";
import { PageHeader } from "../../components/layout/PageHeader";
import { Button, FormError, LoadingBlock } from "../../components/ui/basics";
import { Field, PasswordInput, Select, TextInput } from "../../components/ui/forms";
import { BackLink } from "../../components/ui/BackLink";
import { keys } from "../../api/queryKeys";
import { declaredSecrets, initialProperties, useDataSourceProviders } from "./providers";

/**
 * Creating asks for a name, a provider, and the settings that provider can't connect without.
 *
 * The connection settings depend on the provider — RabbitMQ wants a virtual host, SQS wants a
 * region — so they appear once it is chosen. Only the required ones are asked here: a data source
 * created without them sat on its page with a red error and twenty fields, none of which said
 * which to fill in first. The rest keep their defaults and are on the next screen.
 *
 * Which providers exist, and what each one starts with, comes from the adapters themselves.
 */
export function DataSourceNewPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [name, setName] = useState("");
  const [adapterId, setAdapterId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  // Typed values, by provider, so switching provider and back keeps what was typed.
  const [typed, setTyped] = useState<Record<string, Record<string, string>>>({});

  const providers = useDataSourceProviders();
  // Nothing is chosen until the catalog arrives, so the first provider stands in for a choice the
  // operator has not made yet.
  const provider = providers.data?.find((p) => p.adapterId === adapterId) ?? providers.data?.[0];

  const create = useMutation({
    mutationFn: () => {
      if (!provider) throw new Error("No provider chosen.");
      return api.createDataSource({
        name: name.trim(),
        adapterId: provider.adapterId,
        kind: provider.kind,
        properties: { ...initialProperties(provider), ...(typed[provider.adapterId] ?? {}) },
        secretProperties: declaredSecrets(provider),
      });
    },
    onSuccess: async ({ id }) => {
      await queryClient.invalidateQueries({ queryKey: keys.dataSources.all });
      navigate(`/data-sources/${id}`);
    },
    onError: (e) =>
      setError(e instanceof ApiRequestError ? e.message : "Could not create this data source."),
  });

  const required = provider?.settings.filter((s) => s.required) ?? [];
  const valueOf = (setting: string) =>
    typed[provider?.adapterId ?? ""]?.[setting] ?? provider?.settings.find((s) => s.name === setting)?.default ?? "";
  const setValue = (setting: string, value: string) =>
    setTyped((t) => ({ ...t, [provider!.adapterId]: { ...(t[provider!.adapterId] ?? {}), [setting]: value } }));
  const missing = required.filter((s) => !valueOf(s.name).trim()).map((s) => s.name);

  const submit = () => {
    setError(null);
    if (!name.trim()) {
      setError("A name is required.");
      return;
    }
    create.mutate();
  };

  if (providers.isPending) return <LoadingBlock label="Loading providers…" />;

  // No adapter in the store declares itself a data source provider. Publishing one is the fix,
  // and saying so is more use than an empty menu.
  if (!provider)
    return (
      <div className="max-w-xl">
        <BackLink to="/data-sources" label="Data sources" className="mb-3" />
        <FormError>
          No data source provider adapters are installed. Publish one — bitween.bus.rabbitmq and
          bitween.bus.sqs ship with Bitween — and it will appear here.
        </FormError>
      </div>
    );

  return (
    <div className="max-w-xl">
      <BackLink to="/data-sources" label="Data sources" className="mb-3" />
      <PageHeader
        title="New data source"
        description={
          provider.kind === "Broker"
            ? "A connection Bitween keeps open to a message broker outside it."
            : provider.kind === "Relational"
              ? "A connection Bitween keeps open to a database outside it."
              : "A connection Bitween keeps open to a system outside it."
        }
      />

      <div className="flex flex-col gap-4 rounded-xl border border-ink-200 bg-white p-5">
        <Field label="Name" htmlFor="ds-name">
          <TextInput
            id="ds-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Acme's RabbitMQ"
            autoFocus
          />
        </Field>

        <Field
          label="Provider"
          htmlFor="ds-provider"
          hint={provider.description ?? provider.adapterId}
        >
          <Select
            id="ds-provider"
            value={provider.adapterId}
            onChange={(e) => setAdapterId(e.target.value)}
            options={(providers.data ?? []).map((p) => ({ value: p.adapterId, label: p.label }))}
          />
        </Field>

        {required.length > 0 && (
          <div className="flex flex-col gap-4 border-t border-ink-100 pt-4">
            <p className="text-[13px] text-ink-600">
              What {provider.label} needs to connect. Everything else starts at its default, on the next screen.
            </p>
            {required.map((s) => (
              <Field key={`${provider.adapterId}-${s.name}`} label={s.name} hint={s.hint ?? undefined} htmlFor={`ds-new-${s.name}`}>
                {s.allowedValues ? (
                  <Select
                    id={`ds-new-${s.name}`}
                    value={valueOf(s.name)}
                    onChange={(e) => setValue(s.name, e.target.value)}
                    options={s.allowedValues.map((v) => ({ value: v, label: v }))}
                  />
                ) : s.secret ? (
                  <PasswordInput id={`ds-new-${s.name}`} value={valueOf(s.name)} onChange={(e) => setValue(s.name, e.target.value)} />
                ) : (
                  <TextInput
                    id={`ds-new-${s.name}`}
                    type={s.type === "number" ? "number" : "text"}
                    value={valueOf(s.name)}
                    onChange={(e) => setValue(s.name, e.target.value)}
                  />
                )}
              </Field>
            ))}
          </div>
        )}

        {error && <FormError>{error}</FormError>}

        <div className="flex items-center gap-2">
          <Button onClick={() => navigate("/data-sources")}>Cancel</Button>
          <Button
            variant="primary"
            onClick={submit}
            disabled={create.isPending || missing.length > 0}
            title={missing.length > 0 ? `Still needs ${missing.join(", ")}` : undefined}
          >
            {create.isPending ? "Creating…" : "Create"}
          </Button>
          {missing.length > 0 && <span className="text-[12.5px] text-ink-500">Still needs {missing.join(", ")}.</span>}
        </div>
      </div>
    </div>
  );
}
