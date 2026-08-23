import type { InformationType, InformationTypeDetail, InformationTypeFormat } from "../../api";
import { Checkbox, Field, Select, TextInput } from "../ui/forms";
import { KeyValueEditor, type KvRow } from "../ui/KeyValueEditor";
import { Panel } from "../ui/Panel";
import { BUS_MESSAGE_NAME_PLACEHOLDER, busMessageNameProblem } from "../../lib/busMessageName";

/**
 * Everything about an information type that can be edited, as one component.
 *
 * Shared by the type's own page and by the dialog any picker opens, so the two
 * cannot drift. Read-only context — used-by, exchanges, history — stays on the
 * page, which is the only place with room for it.
 */
export interface InformationTypeDraft {
  name: string;
  code: string;
  format: InformationTypeFormat;
  busEnabled: boolean;
  busMessageTypeName: string;
  duplicateIntervalMinutes: number;
  disregardsUnfilteredMessages: boolean;
  /** key = friendly name, value = the path. */
  promotedProperties: KvRow[];
}

export const informationTypeDraftOf = (t: InformationType): InformationTypeDraft => ({
  name: t.name,
  code: t.code ?? "",
  format: t.format,
  busEnabled: t.busEnabled,
  busMessageTypeName: t.busMessageTypeName ?? "",
  duplicateIntervalMinutes: t.duplicateIntervalMinutes,
  disregardsUnfilteredMessages: t.disregardsUnfilteredMessages,
  promotedProperties: t.promotedProperties.map((p) => ({ key: p.key, value: p.path })),
});

export const EMPTY_INFORMATION_TYPE: InformationTypeDraft = {
  name: "",
  code: "",
  format: "Json",
  busEnabled: false,
  busMessageTypeName: "",
  duplicateIntervalMinutes: 0,
  disregardsUnfilteredMessages: false,
  promotedProperties: [],
};

export const informationTypeDirty = (
  draft: InformationTypeDraft,
  saved: InformationTypeDraft,
): boolean => JSON.stringify(draft) !== JSON.stringify(saved);

/** What the host sends to `updateInformationType`. */
export const informationTypeChanges = (draft: InformationTypeDraft) => ({
  name: draft.name.trim(),
  code: draft.code.trim() || undefined,
  format: draft.format,
  busEnabled: draft.busEnabled,
  busMessageTypeName: draft.busEnabled ? draft.busMessageTypeName.trim() : undefined,
  duplicateIntervalMinutes: draft.duplicateIntervalMinutes,
  disregardsUnfilteredMessages: draft.disregardsUnfilteredMessages,
  promotedProperties: draft.promotedProperties
    .filter((r) => r.key.trim() || r.value.trim())
    .map((r) => ({ key: r.key, path: r.value })),
});

/** Why a draft can't be saved yet, in the operator's words. */
export function informationTypeMissing(draft: InformationTypeDraft): string[] {
  return [
    draft.name.trim().length < 2 && "a name",
    draft.busEnabled && !draft.busMessageTypeName.trim() && "a bus message name",
    // Same rule the field shows under itself, so the gate and the message cannot disagree.
    draft.busEnabled &&
      busMessageNameProblem(draft.busMessageTypeName.trim()) !== null &&
      "a bus message name without spaces",
  ].filter((m): m is string => typeof m === "string");
}

export function InformationTypeFields({
  draft,
  onChange,
  canEdit,
  /** null while it doesn't exist yet — promoted properties arrive with the first save. */
  typeId,
  /** The flow that opened this needs the type on the bus, so the choice is made for it. */
  busRequired = false,
  idPrefix = "it",
}: {
  draft: InformationTypeDraft;
  onChange: (draft: InformationTypeDraft) => void;
  canEdit: boolean;
  typeId: number | null;
  busRequired?: boolean;
  idPrefix?: string;
}) {
  const set = <K extends keyof InformationTypeDraft>(key: K, value: InformationTypeDraft[K]) =>
    onChange({ ...draft, [key]: value });

  return (
    <div className="space-y-5">
      <Panel title="Definition">
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Name" htmlFor={`${idPrefix}-name`}>
            <TextInput
              id={`${idPrefix}-name`}
              value={draft.name}
              disabled={!canEdit}
              placeholder="e.g. Purchase order"
              onChange={(e) => set("name", e.target.value)}
            />
          </Field>
          <Field
            label="Code"
            htmlFor={`${idPrefix}-code`}
            hint="Optional. Renaming it changes how it appears everywhere; existing integrations keep working."
          >
            <TextInput
              id={`${idPrefix}-code`}
              value={draft.code}
              disabled={!canEdit}
              onChange={(e) => set("code", e.target.value.toUpperCase())}
              className="font-mono"
              placeholder="None set"
            />
          </Field>
          <Field label="Payload format" htmlFor={`${idPrefix}-format`}>
            <Select
              id={`${idPrefix}-format`}
              value={draft.format}
              disabled={!canEdit}
              onChange={(e) => set("format", e.target.value as InformationTypeFormat)}
              options={[
                { value: "Json", label: "JSON" },
                { value: "Xml", label: "XML" },
              ]}
            />
          </Field>
          <Field
            label="Duplicate window (minutes)"
            htmlFor={`${idPrefix}-dup`}
            hint="Identical payloads arriving within this window are treated as duplicates. 0 turns it off."
          >
            <TextInput
              id={`${idPrefix}-dup`}
              type="number"
              min={0}
              value={draft.duplicateIntervalMinutes}
              disabled={!canEdit}
              onChange={(e) => set("duplicateIntervalMinutes", Math.max(0, Number(e.target.value)))}
            />
          </Field>
          <div className="space-y-3 sm:col-span-2">
            <Checkbox
              label="Available on the message bus"
              description={
                busRequired
                  ? "Required here — what you are configuring reaches this type over the bus."
                  : "Lets bus gateways listen for this type."
              }
              checked={draft.busEnabled}
              disabled={!canEdit || busRequired}
              onChange={(e) => set("busEnabled", e.target.checked)}
            />
            {draft.busEnabled && (
              <div className="max-w-sm pl-6">
                <Field
                  label="Bus message type name"
                  htmlFor={`${idPrefix}-bus`}
                  hint="Must be unique across information types."
                >
                  <TextInput
                    id={`${idPrefix}-bus`}
                    value={draft.busMessageTypeName}
                    disabled={!canEdit}
                    // Kept as typed. It used to strip whitespace on the way in, so a
                    // pasted "my message" turned into "mymessage" with nothing said —
                    // the same rule the response field states out loud.
                    onChange={(e) => set("busMessageTypeName", e.target.value)}
                    className="font-mono"
                    placeholder={BUS_MESSAGE_NAME_PLACEHOLDER}
                  />
                  {busMessageNameProblem(draft.busMessageTypeName) && (
                    <p className="mt-1 text-[13px] text-danger-700">
                      {busMessageNameProblem(draft.busMessageTypeName)}
                    </p>
                  )}
                </Field>
              </div>
            )}
            <Checkbox
              label="Disregard unfiltered messages"
              description="Drop bus messages of this type that no route matches, instead of failing them."
              checked={draft.disregardsUnfilteredMessages}
              disabled={!canEdit}
              onChange={(e) => set("disregardsUnfilteredMessages", e.target.checked)}
            />
          </div>
        </div>
      </Panel>

      <Panel
        title="Promoted properties"
        description={`Values pulled out of each payload by ${
          draft.format === "Json" ? "JSON path" : "XML path"
        } — routes and filters match on them.`}
      >
        {typeId === null ? (
          <p className="text-[13px] text-ink-500">
            Added once the type exists. Create it and this section opens right here — you won't be
            sent anywhere.
          </p>
        ) : (
          <KeyValueEditor
            rows={draft.promotedProperties}
            onChange={(promotedProperties) => set("promotedProperties", promotedProperties)}
            keyLabel="Friendly name"
            valueLabel={draft.format === "Xml" ? "XML path" : "JSON path"}
            keyPlaceholder="OrderNumber"
            valuePlaceholder={draft.format === "Xml" ? "//Order/Number" : "$.order.id"}
            editable={canEdit}
            emptyText="No promoted properties — routes can only match on the whole payload."
          />
        )}
      </Panel>
    </div>
  );
}

/** The page passes its detail straight through; the dialog only ever has the base type. */
export const draftOfDetail = (t: InformationTypeDetail): InformationTypeDraft =>
  informationTypeDraftOf(t);
