import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, ChevronDown, ChevronRight } from "lucide-react";
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
  // Code and format are auto-derived — tucked away unless someone wants them.
  const [detailsOpen, setDetailsOpen] = useState(false);

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
    // The code field can be emptied and then collapsed away — surface it again.
    if (!code.trim()) {
      setDetailsOpen(true);
      return;
    }
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
        {/* Auto-derived identity: visible, but out of the way unless changed. */}
        <div className="rounded-lg bg-ink-50 px-3 py-2.5">
          <button
            type="button"
            onClick={() => setDetailsOpen((o) => !o)}
            aria-expanded={detailsOpen}
            className="flex w-full items-center gap-1.5 text-[13px] text-ink-500 hover:text-ink-700"
          >
            {detailsOpen ? (
              <ChevronDown className="size-3.5 shrink-0" aria-hidden />
            ) : (
              <ChevronRight className="size-3.5 shrink-0" aria-hidden />
            )}
            <span className="min-w-0 truncate text-left">
              Code{" "}
              {code ? (
                <code className="rounded bg-white px-1.5 py-0.5 font-mono text-[11px] text-ink-700 ring-1 ring-ink-200">
                  {code}
                </code>
              ) : (
                <span className="italic">suggested from the name</span>
              )}{" "}
              · stored as {format === "Json" ? "JSON" : "XML"}
            </span>
            {!detailsOpen && <span className="ml-auto shrink-0 font-medium text-crimson-700">Change</span>}
          </button>
          {detailsOpen && (
            <div className="mt-3 space-y-4">
              <Field
                label="Code"
                htmlFor="nit-code"
                hint="Short identity shown across the system. Uppercase letters, digits and underscores."
              >
                <TextInput
                  id="nit-code"
                  required
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
          )}
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
              <Field label="Bus message type name" htmlFor="nit-bus" hint="Must be unique across information types.">
                <TextInput
                  id="nit-bus"
                  required
                  value={busMessageTypeName}
                  onChange={(e) => setBusMessageTypeName(e.target.value)}
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
