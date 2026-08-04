import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Plus } from "lucide-react";
import { api } from "../../../api";
import { useSessionCan } from "../../../auth/guards";
import { Field, TextInput } from "../../../components/ui/forms";
import { SearchSelect } from "../../../components/ui/SearchSelect";
import { InformationTypeDialog } from "../../../components/config/InformationTypeDialog";

/**
 * The Response stage's body: what happens to whatever the delivery hands back.
 *
 * Shared by the studio pages and both create pages so the node means the same
 * thing wherever it appears — the create rails would otherwise be one node
 * shorter than the edit rail, which defeats reusing the pipeline at all.
 */
export function ResponseFields({
  handlerId,
  responseIntegrationId,
  responseMessageTypeName,
  onChange,
  disabled,
  candidates,
  idPrefix = "resp",
}: {
  /** Nothing is delivered without a handler, so there is no response to route. */
  handlerId: string | null;
  responseIntegrationId: number | null;
  responseMessageTypeName: string | null;
  onChange: (patch: {
    responseIntegrationId?: number | null;
    responseMessageTypeName?: string | null;
  }) => void;
  disabled: boolean;
  /** Integrations the response can be fed into (excluding this one). */
  candidates: { id: number; name: string }[];
  idPrefix?: string;
}) {
  if (handlerId === null)
    return (
      <p className="text-sm text-ink-500">
        Nothing is delivered, so there is no response to route. Pick a delivery step first.
      </p>
    );

  return (
    <div className="grid gap-4 sm:grid-cols-2">
      <Field
        label="Feed the response into"
        htmlFor={`${idPrefix}-into`}
        hint="Chains the delivery response into another integration."
      >
        <SearchSelect
          id={`${idPrefix}-into`}
          value={responseIntegrationId === null ? "" : String(responseIntegrationId)}
          disabled={disabled}
          onChange={(v) => onChange({ responseIntegrationId: v === "" ? null : Number(v) })}
          clearLabel="Nothing — responses are only recorded"
          options={candidates.map((x) => ({ value: String(x.id), label: x.name }))}
        />
      </Field>
      <BusMessageField
        value={responseMessageTypeName}
        disabled={disabled}
        idPrefix={idPrefix}
        onChange={(responseMessageTypeName) => onChange({ responseMessageTypeName })}
      />
    </div>
  );
}

/**
 * Which bus message the response is published as.
 *
 * A picker rather than a text box, because the name is not free-form in practice:
 * `BusService` consumes exactly the bus message names of bus-enabled information
 * types, so a typo here doesn't publish to nobody-in-particular — it publishes to
 * nobody at all, silently. Listing the real names makes the working answers the
 * easy ones, and offers to create the type when it doesn't exist yet.
 *
 * Free text is still reachable, because publishing is not Bitween's to police:
 * something outside the product may be the consumer. It is just no longer the
 * default, and an unrecognised name says so out loud.
 */
function BusMessageField({
  value,
  disabled,
  idPrefix,
  onChange,
}: {
  value: string | null;
  disabled: boolean;
  idPrefix: string;
  onChange: (value: string | null) => void;
}) {
  const informationTypes = useQuery({
    queryKey: ["information-types"],
    queryFn: () => api.listInformationTypes(),
    staleTime: Infinity,
  });
  const canCreate = useSessionCan("documents.create");
  const [creating, setCreating] = useState(false);

  const known = (informationTypes.data ?? []).filter((t) => t.busEnabled && t.busMessageTypeName);
  const matched = known.find((t) => t.busMessageTypeName?.toLowerCase() === (value ?? "").toLowerCase());
  // A saved value nobody carries: kept as an option so opening this panel can
  // never silently drop what is already configured.
  const unknown = value !== null && value !== "" && !matched;
  const [freeText, setFreeText] = useState(unknown);

  if (freeText)
    return (
      <Field
        label="Publish the response on the bus as"
        htmlFor={`${idPrefix}-bus`}
        hint="A name of your own. Nothing in Bitween listens for it unless an information type carries it."
      >
        <TextInput
          id={`${idPrefix}-bus`}
          value={value ?? ""}
          disabled={disabled}
          placeholder="e.g. order-confirmation"
          className="font-mono"
          onChange={(e) => onChange(e.target.value || null)}
        />
        {!disabled && (
          <button
            type="button"
            onClick={() => setFreeText(false)}
            className="mt-1 text-[13px] font-medium text-crimson-700 hover:underline"
          >
            Pick a known message instead
          </button>
        )}
      </Field>
    );

  return (
    <Field
      label="Publish the response on the bus as"
      htmlFor={`${idPrefix}-bus`}
      hint="Every route bound to that message's information type picks it up — on any gateway."
    >
      <SearchSelect
        id={`${idPrefix}-bus`}
        value={matched?.busMessageTypeName ?? ""}
        disabled={disabled || informationTypes.isPending}
        onChange={(v) => onChange(v === "" ? null : v)}
        clearLabel="Nothing — keep responses off the bus"
        options={known.map((t) => ({
          value: t.busMessageTypeName!,
          label: t.busMessageTypeName!,
          code: t.code,
          hint: t.name,
        }))}
      />
      {!disabled && (
        <div className="mt-1 flex flex-wrap items-center gap-3">
          {canCreate && (
            <button
              type="button"
              onClick={() => setCreating(true)}
              className="inline-flex items-center gap-1 text-[13px] font-medium text-crimson-700 hover:underline"
            >
              <Plus className="size-3" /> New information type
            </button>
          )}
          <button
            type="button"
            onClick={() => setFreeText(true)}
            className="text-[13px] font-medium text-crimson-700 hover:underline"
          >
            Use a name of your own
          </button>
        </div>
      )}
      {creating && (
        // Bus-enabled is not optional here: this field is what gets published, and a
        // type with no bus message name cannot answer it.
        <InformationTypeDialog
          typeId={null}
          busRequired
          onClose={() => setCreating(false)}
          onSaved={({ busMessageTypeName }) => onChange(busMessageTypeName || null)}
        />
      )}
    </Field>
  );
}
