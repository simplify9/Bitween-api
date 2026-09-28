import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ArrowUpRight, Plus } from "lucide-react";
import { api, type SubscriptionType } from "../../../api";
import { useSessionCan } from "../../../auth/guards";
import { Field } from "../../../components/ui/forms";
import { Button } from "../../../components/ui/basics";
import { CodeBadge } from "../../../components/ui/Panel";
import {
  SubscriptionStatusBadges,
  TypeBadge,
  useSubscriptionRowsById,
  useSubscriptionsCache,
} from "../../../components/config/shared";
import { SearchSelect } from "../../../components/ui/SearchSelect";
import { InformationTypeDialog } from "../../../components/config/InformationTypeDialog";
import { busMessageNameProblem } from "../../../lib/busMessageName";
import { keys } from "../../../api/queryKeys";

/**
 * The Response stage's body: what happens to whatever the delivery hands back.
 *
 * Shared by the studio pages and both create pages so the node means the same
 * thing wherever it appears — the create rails would otherwise be one node
 * shorter than the edit rail, which defeats reusing the pipeline at all.
 *
 * Two ways to pass a response on, and both can be set: hand it to a response
 * subscription, which runs its own pipeline on it, or publish it on the bus.
 */
export function ResponseFields({
  handlerId,
  responseSubscriptionId,
  responseMessageTypeName,
  onChange,
  disabled,
  candidates,
  idPrefix = "resp",
  onNewResponseSubscription,
  onOpenResponseSubscription,
}: {
  /** Nothing is delivered without a handler, so there is no response to route. */
  handlerId: string | null;
  responseSubscriptionId: number | null;
  responseMessageTypeName: string | null;
  onChange: (patch: {
    responseSubscriptionId?: number | null;
    responseMessageTypeName?: string | null;
  }) => void;
  disabled: boolean;
  /** Every other subscription. Only the Response ones can be picked; the rest name a saved target. */
  candidates: { id: number; name: string; type: SubscriptionType }[];
  idPrefix?: string;
  /**
   * Starts a new response subscription the way this page makes new things: the bus gateway
   * draws its cards on the canvas, every other page opens its create page and comes back.
   * Left out, nothing is offered.
   */
  onNewResponseSubscription?: () => void;
  /**
   * Opens the chosen one, to see or change it and go on down its own chain. Each page decides
   * how: a saved page offers to save first, a create page keeps its draft for the way back,
   * the bus gateway canvas opens it as the next hop.
   */
  onOpenResponseSubscription?: (id: number) => void;
}) {
  if (handlerId === null)
    return (
      <p className="text-sm text-ink-500">
        Nothing is delivered, so there is no response to route. Pick a delivery step first.
      </p>
    );

  const chosen = candidates.find((x) => x.id === responseSubscriptionId);

  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2">
        <ResponseSubscriptionField
          value={responseSubscriptionId}
          candidates={candidates}
          disabled={disabled}
          idPrefix={idPrefix}
          onChange={(responseSubscriptionId) => onChange({ responseSubscriptionId })}
          onNew={onNewResponseSubscription}
        />
        <BusMessageField
          value={responseMessageTypeName}
          disabled={disabled}
          idPrefix={idPrefix}
          onChange={(responseMessageTypeName) => onChange({ responseMessageTypeName })}
        />
      </div>
      {chosen && <ResponseTargetCard target={chosen} onOpen={onOpenResponseSubscription} />}
    </div>
  );
}

/**
 * Which response subscription the response is handed to.
 *
 * Only Response-type subscriptions are offered, because a response is the only way one
 * runs — every other type has an entry point of its own, and feeding it a response runs
 * it through a door it does not have. A legacy target already saved stays listed, so
 * opening this panel can't quietly blank it; once changed, it can't be picked again.
 */
function ResponseSubscriptionField({
  value,
  candidates,
  disabled,
  idPrefix,
  onChange,
  onNew,
}: {
  value: number | null;
  candidates: { id: number; name: string; type: SubscriptionType }[];
  disabled: boolean;
  idPrefix: string;
  onChange: (value: number | null) => void;
  onNew?: () => void;
}) {
  const canCreate = useSessionCan("subscriptions.create");

  const offered = candidates.filter((x) => x.type === "Response" || x.id === value);
  // Saved, but no longer in the list: deleted, or not loaded yet. Listed so the field shows it.
  const missing = value !== null && !offered.some((x) => x.id === value);

  return (
    <Field
      label="Hand the response to"
      htmlFor={`${idPrefix}-into`}
      hint="A response subscription runs its own pipeline on it, as this subscription's partner."
    >
      <SearchSelect
        id={`${idPrefix}-into`}
        value={value === null ? "" : String(value)}
        disabled={disabled}
        onChange={(v) => onChange(v === "" ? null : Number(v))}
        clearLabel="Nothing — no response subscription"
        options={[
          ...offered.map((x) => ({
            value: String(x.id),
            label: x.name,
            hint: x.type === "Response" ? undefined : "legacy",
          })),
          ...(missing ? [{ value: String(value), label: `Subscription ${value}` }] : []),
        ]}
      />
      {!disabled && canCreate && onNew && (
        <div className="mt-1">
          <button
            type="button"
            onClick={onNew}
            title="Define a new response subscription and hand the response to it."
            className="inline-flex items-center gap-1 text-[13px] font-medium text-crimson-700 hover:underline"
          >
            <Plus className="size-3" /> New response subscription
          </button>
        </div>
      )}
    </Field>
  );
}

/**
 * The subscription the response goes to, summed up — what it carries, whether it runs, and
 * where its own response goes next — with the way to open it.
 *
 * A legacy Internal or ApiCall target, from before the Response type existed, says so: it
 * keeps working, because dropping it would change what a live subscription does without
 * anyone being told, but nothing new can pick one.
 */
function ResponseTargetCard({
  target,
  onOpen,
}: {
  target: { id: number; name: string; type: SubscriptionType };
  onOpen?: (id: number) => void;
}) {
  const setups = useSubscriptionsCache().data;
  const rows = useSubscriptionRowsById();
  // Not there for one still being defined on the bus gateway canvas — it has no id yet.
  const setup = setups?.find((x) => x.id === target.id);
  const row = rows.get(target.id);
  const next = setup?.responseSubscriptionId
    ? (setups?.find((x) => x.id === setup.responseSubscriptionId)?.name ?? `subscription ${setup.responseSubscriptionId}`)
    : null;
  const legacy = target.type !== "Response";

  return (
    <div className="rounded-xl border border-ink-200 bg-ink-50/60 p-3">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0 space-y-1">
          <p className="flex flex-wrap items-center gap-2 text-[14px] font-semibold text-ink-900">
            {target.name}
            <TypeBadge type={target.type} />
            {row && <SubscriptionStatusBadges enabled={row.enabled} paused={row.paused} />}
          </p>
          {setup ? (
            <p className="flex flex-wrap items-center gap-x-1.5 gap-y-1 text-[13px] text-ink-600">
              {row && (
                <>
                  Carries <CodeBadge code={row.informationTypeCode} name={row.informationTypeCode} />
                  <span className="text-ink-300">·</span>
                </>
              )}
              <span title="Where this one's own delivery response goes">
                {setup.handlerId === null
                  ? "delivers nothing, so the chain ends here"
                  : next
                    ? <>then hands its response to <span className="font-medium text-ink-800">{next}</span></>
                    : setup.responseMessageTypeName
                      ? <>then publishes its response as <code className="font-mono text-[12px]">{setup.responseMessageTypeName}</code></>
                      : "then records its response, and the chain ends here"}
              </span>
            </p>
          ) : (
            <p className="text-[13px] text-ink-500">Being defined here — it is saved with this one.</p>
          )}
        </div>
        {onOpen && (
          <Button
            size="sm"
            onClick={() => onOpen(target.id)}
            title="Open it to see or change its pipeline, and follow its own response on from there."
          >
            Open <ArrowUpRight className="size-3.5" />
          </Button>
        )}
      </div>
      {legacy && (
        <p className="mt-2 border-t border-ink-200 pt-2 text-[12px] text-ink-500">
          A legacy subscription. It keeps getting the response, but once you change this it can't be
          picked again — only response subscriptions can.
        </p>
      )}
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
    queryKey: keys.informationTypes.list,
    queryFn: () => api.listInformationTypes(),
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
