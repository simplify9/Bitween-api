import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Plus } from "lucide-react";
import { api, type IntegrationType } from "../../../api";
import { useSessionCan } from "../../../auth/guards";
import { Field } from "../../../components/ui/forms";
import { SearchSelect } from "../../../components/ui/SearchSelect";
import { InformationTypeDialog } from "../../../components/config/InformationTypeDialog";
import { busMessageNameProblem } from "../../../lib/busMessageName";

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
  candidates: { id: number; name: string; type: IntegrationType }[];
  idPrefix?: string;
}) {
  if (handlerId === null)
    return (
      <p className="text-sm text-ink-500">
        Nothing is delivered, so there is no response to route. Pick a delivery step first.
      </p>
    );

  // A bus gateway's routes are chosen by the message that runs them. Feeding a response
  // straight into one runs that single route with the bus skipped — nothing published, no
  // matching, no filter, and none of the other routes bound to the same message — which
  // looks like publishing and isn't. The API refuses it; this keeps it out of reach.
  // An already-saved one stays listed so opening this panel can't quietly blank it.
  const offered = candidates.filter((x) => x.type !== "BusGateway" || x.id === responseIntegrationId);
  const busRouteChosen = candidates.some(
    (x) => x.id === responseIntegrationId && x.type === "BusGateway",
  );

  return (
    <div className="grid gap-4 sm:grid-cols-2">
      <Field
        label="Feed the response into"
        htmlFor={`${idPrefix}-into`}
        hint="Hands the delivery response straight to another integration, off the bus."
      >
        <SearchSelect
          id={`${idPrefix}-into`}
          value={responseIntegrationId === null ? "" : String(responseIntegrationId)}
          disabled={disabled}
          onChange={(v) => onChange({ responseIntegrationId: v === "" ? null : Number(v) })}
          clearLabel="Nothing — responses are only recorded"
          options={offered.map((x) => ({ value: String(x.id), label: x.name }))}
        />
        {busRouteChosen && (
          <p className="mt-1 text-[13px] text-danger-700">
            That is a bus gateway route, which saving will refuse — it would run on its own with
            the bus skipped. Publish on the bus below instead, and its gateway picks it up.
          </p>
        )}
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
 * A name of your own is still reachable, because publishing is not Bitween's to
 * police: something outside the product may be the consumer. It is offered as the
 * last row of the same dropdown rather than behind a separate mode — the mode used
 * to be seeded from `unknown`, which is derived from a query that has not resolved
 * on first render, so it latched on for every value including the valid ones.
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
  // A saved value nobody carries. Only meaningful once the types have actually arrived —
  // while the query is pending `known` is empty, so every value looks unknown.
  const unknown = !informationTypes.isPending && value !== null && value !== "" && !matched;

  return (
    <Field
      label="Publish the response on the bus as"
      htmlFor={`${idPrefix}-bus`}
      hint="Every route bound to that message's information type picks it up — on any gateway."
    >
      <SearchSelect
        id={`${idPrefix}-bus`}
        value={matched?.busMessageTypeName ?? value ?? ""}
        disabled={disabled || informationTypes.isPending}
        onChange={(v) => onChange(v === "" ? null : v)}
        clearLabel="Nothing — keep responses off the bus"
        // Publishing is not Bitween's to police — the consumer may be another
        // product entirely — so a name nobody carries is offered right here
        // rather than dead-ending on "nothing matches".
        freeText={(typed) =>
          busMessageNameProblem(typed) ??
          { value: typed, label: `Publish as “${typed}” — a name of your own` }
        }
        options={[
          ...known.map((t) => ({
            value: t.busMessageTypeName!,
            label: t.busMessageTypeName!,
            code: t.code,
            hint: t.name,
          })),
          // A saved or just-accepted name no information type carries. Listed so the
          // field can display it, and marked so it doesn't read as a working choice.
          ...(unknown ? [{ value: value!, label: value!, hint: "not an information type" }] : []),
        ]}
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
