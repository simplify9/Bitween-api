import { Field, TextInput } from "../../../components/ui/forms";
import { SearchSelect } from "../../../components/ui/SearchSelect";

/**
 * The Response stage's body: what happens to whatever the delivery hands back.
 *
 * Shared by the studio page and both create pages so the node means the same
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
      <Field
        label="Publish the response on the bus as"
        htmlFor={`${idPrefix}-bus`}
        hint="Leave empty to keep responses off the bus."
      >
        <TextInput
          id={`${idPrefix}-bus`}
          value={responseMessageTypeName ?? ""}
          disabled={disabled}
          placeholder="e.g. order-confirmation"
          className="font-mono"
          onChange={(e) => onChange({ responseMessageTypeName: e.target.value || null })}
        />
      </Field>
    </div>
  );
}
