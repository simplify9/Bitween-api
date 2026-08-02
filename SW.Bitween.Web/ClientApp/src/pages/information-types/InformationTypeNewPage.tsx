import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import { api, type InformationTypeFormat } from "../../api";
import { Button, FormError } from "../../components/ui/basics";
import { Checkbox, Field, Select, TextInput } from "../../components/ui/forms";
import { ReturnBanner } from "../../components/ui/ReturnBanner";
import { suggestCode } from "../../lib/identifiers";
import { useReturnContext, withReturn } from "../../lib/returnTo";

export function InformationTypeNewPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const ctx = useReturnContext();
  const [name, setName] = useState("");
  const [code, setCode] = useState("");
  const [codeTouched, setCodeTouched] = useState(false);
  const [format, setFormat] = useState<InformationTypeFormat>("Json");
  const [busEnabled, setBusEnabled] = useState(false);
  const [busMessageTypeName, setBusMessageTypeName] = useState("");

  const create = useMutation({
    mutationFn: () =>
      api.createInformationType({
        name,
        code,
        format,
        busEnabled,
        busMessageTypeName: busEnabled ? busMessageTypeName : undefined,
      }),
    onSuccess: (type) => {
      void queryClient.invalidateQueries({ queryKey: ["information-types"] });
      const base = `/information-types/${type.id}`;
      navigate(ctx ? `${withReturn(base, ctx)}&picked=infotype:${type.id}` : base);
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    create.mutate();
  };

  return (
    <div>
      <Link
        to={ctx?.to ?? "/information-types"}
        className="mb-4 inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800"
      >
        <ArrowLeft className="size-3.5" /> {ctx ? "Back without creating" : "Information types"}
      </Link>

      <ReturnBanner />

      <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">New information type</h1>
      <p className="mt-1 text-sm text-ink-500">
        Bus settings and promoted properties are configured afterwards on its page.
      </p>

      <form onSubmit={submit} className="mt-6 max-w-lg space-y-4 rounded-xl border border-ink-200 bg-white p-5">
        <Field label="Name" htmlFor="nit-name">
          <TextInput
            id="nit-name"
            required
            autoFocus
            value={name}
            onChange={(e) => {
              setName(e.target.value);
              if (!codeTouched) setCode(suggestCode(e.target.value));
            }}
            placeholder="e.g. Purchase order"
          />
        </Field>
        {/* Code is auto-derived from the name until edited — shown, not hidden. */}
        <div className="grid gap-4 sm:grid-cols-2">
          <Field
            label="Code"
            htmlFor="nit-code"
            hint="Optional short identity shown across the system. Derived from the name until you edit it."
          >
            <TextInput
              id="nit-code"
              value={code}
              onChange={(e) => {
                setCodeTouched(true);
                setCode(e.target.value.toUpperCase());
              }}
              placeholder="PURCHASE_ORDER"
              className="font-mono"
            />
          </Field>
          <Field label="Format" htmlFor="nit-format">
            <Select
              id="nit-format"
              value={format}
              onChange={(e) => setFormat(e.target.value as InformationTypeFormat)}
              options={[
                { value: "Json", label: "JSON" },
                { value: "Xml", label: "XML" },
              ]}
            />
          </Field>
        </div>
        <div className="space-y-3">
          <Checkbox
            label="Available on the message bus"
            description="Lets bus gateways listen for this type."
            checked={busEnabled}
            onChange={(e) => setBusEnabled(e.target.checked)}
          />
          {busEnabled && (
            <div className="max-w-sm pl-6">
              <Field
                label="Bus message type name"
                htmlFor="nit-bus"
                hint="Must be unique across information types. No spaces."
              >
                <TextInput
                  id="nit-bus"
                  required
                  value={busMessageTypeName}
                  onChange={(e) => setBusMessageTypeName(e.target.value.replace(/\s+/g, ""))}
                  className="font-mono"
                  placeholder="purchase-order"
                />
              </Field>
            </div>
          )}
        </div>
        <FormError>{create.error?.message}</FormError>
        <div className="flex justify-end">
          <Button type="submit" variant="primary" busy={create.isPending}>
            Create information type
          </Button>
        </div>
      </form>
    </div>
  );
}
