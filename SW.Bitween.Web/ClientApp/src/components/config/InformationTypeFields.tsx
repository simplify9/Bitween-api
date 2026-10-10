import { type InformationType, type InformationTypeFormat } from "../../api";
import { Button } from "../ui/basics";
import { Checkbox, Field, Select, TextInput } from "../ui/forms";
import { KeyValueEditor, type KvRow } from "../ui/KeyValueEditor";
import { Panel } from "../ui/Panel";
import { ConfirmDialog } from "../ui/overlays";
import { BUS_MESSAGE_NAME_PLACEHOLDER, busMessageNameProblem } from "../../lib/busMessageName";
import {
  CARRIED_FORMAT_NOTE,
  formatLabel,
  INFORMATION_TYPE_FORMATS,
  readsContent,
} from "../../lib/informationTypeFormat";
import type { InformationTypeField } from "./informationTypeErrors";
import { useInformationTypeMessages } from "./informationTypeDraft";
import { hasPromotedRows } from "./informationTypeDraft";

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
  /** Empty for none. */
  validationSchema: string;
}

/** The messages that go with a type's bus queue, which is deleted along with the type or when it's renamed. */
export function InformationTypeMessagesWarning({ typeId }: { typeId: number }) {
  const queued = useInformationTypeMessages(typeId);
  if (queued === 0) return null;
  return (
    <p className={queued === null ? undefined : "font-medium text-danger-700"}>
      {queued === null
        ? "Any messages still in it are deleted too."
        : `${queued === 1 ? "1 message is" : `${queued} messages are`} still in it and will be deleted.`}{" "}
      They're the incoming messages themselves, so they can't be recovered.
    </p>
  );
}

/** Asked before saving a new bus message type name, which moves the type to a new queue. */
export function BusTypeRenameConfirm({
  typeId,
  from,
  to,
  onConfirm,
  onClose,
}: {
  typeId: number;
  from: string;
  to: string;
  onConfirm: () => Promise<void>;
  onClose: () => void;
}) {
  const name = (n: string) => <code className="font-mono text-xs text-ink-800">{n}</code>;
  return (
    <ConfirmDialog
      title="Change the bus message type name?"
      body={
        <div className="space-y-2">
          <p>
            Messages sent as {name(to)} go to a new queue. The queue for {name(from)} is deleted, and
            anything still sent as {name(from)} is dropped.
          </p>
          <InformationTypeMessagesWarning typeId={typeId} />
        </div>
      }
      confirmLabel="Change and save"
      onConfirm={onConfirm}
      onClose={onClose}
    />
  );
}


export function InformationTypeFields({
  draft,
  onChange,
  canEdit,
  /** The flow that opened this needs the type on the bus, so the choice is made for it. */
  busRequired = false,
  idPrefix = "it",
  saved,
  errors = {},
}: {
  draft: InformationTypeDraft;
  onChange: (draft: InformationTypeDraft) => void;
  canEdit: boolean;
  busRequired?: boolean;
  idPrefix?: string;
  /** The type as stored, when editing one: what turning the bus off pauses. */
  saved?: InformationType;
  /** What the server refused a save over, by the field it was about. */
  errors?: Partial<Record<InformationTypeField, string>>;
}) {
  const storedName = saved?.busMessageTypeName?.trim() || null;
  const waiting = useInformationTypeMessages(storedName && !draft.busEnabled ? saved!.id : null);
  const set = <K extends keyof InformationTypeDraft>(key: K, value: InformationTypeDraft[K]) =>
    onChange({ ...draft, [key]: value });

  return (
    <div className="space-y-5">
      <Panel title="Definition">
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Name" htmlFor={`${idPrefix}-name`} error={errors.name}>
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
            error={errors.code}
            hint="Optional. Renaming it changes how it appears everywhere; existing subscriptions keep working."
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
          <Field
            label="Payload format"
            htmlFor={`${idPrefix}-format`}
            hint={readsContent(draft.format) ? undefined : CARRIED_FORMAT_NOTE}
          >
            <Select
              id={`${idPrefix}-format`}
              value={draft.format}
              disabled={!canEdit}
              onChange={(e) => set("format", e.target.value as InformationTypeFormat)}
              options={INFORMATION_TYPE_FORMATS}
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
                busRequired ? (
                  "Required here — what you are configuring reaches this type over the bus."
                ) : !storedName ? (
                  "Lets bus gateways listen for this type."
                ) : draft.busEnabled ? (
                  "Lets bus gateways listen for this type. Turning it off pauses it: its queue keeps collecting messages, and they're processed when you turn it back on."
                ) : (
                  <>
                    {saved!.busEnabled ? "Saving pauses it: messages" : "Paused: messages"} sent as{" "}
                    <code className="font-mono text-xs">{storedName}</code> wait in its queue, and
                    they're processed when you turn this back on.
                    {!!waiting && ` ${waiting} ${waiting === 1 ? "is" : "are"} waiting now.`}
                  </>
                )
              }
              checked={draft.busEnabled}
              disabled={!canEdit || busRequired}
              // Off puts the stored name back, so what is saved is a pause of the queue it has,
              // never a rename hidden behind a field that is no longer shown.
              onChange={(e) =>
                onChange({
                  ...draft,
                  busEnabled: e.target.checked,
                  busMessageTypeName: e.target.checked ? draft.busMessageTypeName : (storedName ?? ""),
                })
              }
            />
            {draft.busEnabled && (
              <div className="max-w-sm pl-6">
                <Field
                  label="Bus message type name"
                  htmlFor={`${idPrefix}-bus`}
                  error={errors.bus}
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

      {readsContent(draft.format) ? (
        <Panel
          title="Promoted properties"
          description={`Values pulled out of each payload by ${
            draft.format === "Json" ? "JSON path" : "XML path"
          } — routes and filters match on them. They're shown in this order on the Exchanges page; drag to change it. Point them at short values, such as an order number: a value longer than 500 characters is stored cut.`}
        >
          <KeyValueEditor
            rows={draft.promotedProperties}
            onChange={(promotedProperties) => set("promotedProperties", promotedProperties)}
            reorderable={{
              first: {
                label: "Main",
                title:
                  "The main property: shown first on the Exchanges page, and its value names the exchange's file when the retention job archives it.",
              },
            }}
            keyLabel="Name"
            addLabel="Add promoted property"
            valueLabel={draft.format === "Xml" ? "XML path" : "JSON path"}
            keyPlaceholder="OrderNumber"
            valuePlaceholder={draft.format === "Xml" ? "//Order/Number" : "$.order.id"}
            editable={canEdit}
            emptyText="No promoted properties — routes can only match on the whole payload."
          />
        </Panel>
      ) : (
        <Panel
          title="Promoted properties"
          description={`None — ${formatLabel(draft.format)} content isn't read, so there is nothing to pull values from.`}
        >
          {/* Only reachable by switching a type that still promotes something, which the server
              refuses to save. */}
          {hasPromotedRows(draft) && (
            <div className="flex flex-wrap items-center gap-3">
              <p role="alert" className="text-[13px] text-danger-700">
                Still promotes {draft.promotedProperties.map((r) => r.key.trim() || r.value.trim()).join(", ")} —{" "}
                {formatLabel(draft.format)} types can't have promoted properties.
              </p>
              {canEdit && (
                <Button size="sm" onClick={() => set("promotedProperties", [])}>
                  Remove them
                </Button>
              )}
            </div>
          )}
        </Panel>
      )}

      {readsContent(draft.format) ? (
        <Panel
          title="Schema"
          description={`${
            draft.format === "Json" ? "A JSON Schema" : "An XSD"
          } every payload of this type must match. A call to an API gateway that doesn't is refused with what's wrong; any other exchange whose input doesn't fails, and says why. Leave it empty to accept anything. It can't refer to other files or URLs.`}
        >
          <textarea
            aria-label="Schema"
            value={draft.validationSchema}
            onChange={(e) => set("validationSchema", e.target.value)}
            readOnly={!canEdit}
            spellCheck={false}
            rows={10}
            placeholder={
              draft.format === "Json"
                ? '{ "type": "object", "required": ["orderId"] }'
                : '<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">…</xs:schema>'
            }
            className="w-full rounded-lg border border-ink-200 bg-white px-3 py-2 font-mono text-[12.5px] text-ink-800 focus:border-focus-500 focus:outline-none"
          />
        </Panel>
      ) : (
        draft.validationSchema.trim() !== "" && (
          <Panel title="Schema" description={`${formatLabel(draft.format)} content isn't read, so it can't be checked.`}>
            <div className="flex flex-wrap items-center gap-3">
              <p role="alert" className="text-[13px] text-danger-700">
                Still has a schema — {formatLabel(draft.format)} types can't have one.
              </p>
              {canEdit && (
                <Button size="sm" onClick={() => set("validationSchema", "")}>
                  Remove it
                </Button>
              )}
            </div>
          </Panel>
        )
      )}
    </div>
  );
}
