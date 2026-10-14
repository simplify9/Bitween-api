import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, Trash2 } from "lucide-react";
import { api, type RetryAlertConfig, type RetryDelay, type RetryGroup, type RetryMatcher, type RetryResultType } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSessionCan } from "../../auth/useSessionCan";
import { useLeaveGuard } from "../../lib/useLeaveGuard";
import { useCrumbTitle, usePageTitle } from "../../lib/pageTitle";
import { BackLink } from "../../components/ui/BackLink";
import { FieldProblem, StillNeeds, useFormCheck, type FormNeed } from "../../components/ui/formCheck";
import { Button, EmptyState, FormError, LoadingBlock } from "../../components/ui/basics";
import { Checkbox, Field, Select, TextInput } from "../../components/ui/forms";
import { DeleteDialog, counted } from "../../components/ui/DeleteDialog";
import { MoreActionsMenu } from "../../components/layout/HeaderActions";
import { EditableTitle, Panel } from "../../components/ui/Panel";
import { safeReturn, useDetour } from "../../lib/detour";
import { useNotificationChannelNames } from "../../lib/notificationChannels";
import { AlertRouting } from "./AlertRouting";

const DEFAULT_MATCHER: RetryMatcher = { type: "contains", value: "", caseSensitive: false };

const defaultMatcherFor = (type: RetryMatcher["type"]): RetryMatcher => {
  switch (type) {
    case "contains":
      return { type, value: "", caseSensitive: false };
    case "regex":
      return { type, pattern: "", flags: "i" };
    case "exceptionType":
      return { type, value: "", includeInner: true };
    case "jsonPath":
      return { type, path: "$.", op: "Eq", value: "" };
  }
};

const defaultDelayFor = (type: RetryDelay["type"]): RetryDelay => {
  switch (type) {
    case "fixed":
      return { type, delaySeconds: 60 };
    case "linear":
      return { type, initialSeconds: 60, incrementSeconds: 60 };
    case "exponential":
      return { type, initialSeconds: 30, multiplier: 2, maxSeconds: 900 };
  }
};

function MatcherRow({
  matcher,
  onChange,
  onRemove,
}: {
  matcher: RetryMatcher;
  onChange: (m: RetryMatcher) => void;
  onRemove: () => void;
}) {
  /*
    The free-text field in a condition row: an error fragment, an exception name, a
    comparison value. The controls beside it are fixed-width pickers whose longest
    option is known, so an equal share left this one the narrowest thing in the row
    while being the only one holding arbitrary text - "INVALID_DECL" clipped mid-word.
    Twice the share of what's left, and a floor wide enough for a real value; the row
    already wraps, so below that it takes a line of its own rather than shrinking.
  */
  const num = "flex-[2] min-w-48";
  return (
    <div className="flex flex-wrap items-center gap-2 rounded-lg border border-ink-200 p-2.5">
      <Select
        aria-label="Condition type"
        value={matcher.type}
        onChange={(e) => onChange(defaultMatcherFor(e.target.value as RetryMatcher["type"]))}
        className="!w-40"
        options={[
          { value: "contains", label: "Error contains" },
          { value: "regex", label: "Error matches regex" },
          { value: "exceptionType", label: "Exception type" },
          { value: "jsonPath", label: "Result JSON path" },
        ]}
      />
      {matcher.type === "contains" && (
        <>
          <TextInput
            aria-label="Text to find"
            className={num}
            value={matcher.value}
            onChange={(e) => onChange({ ...matcher, value: e.target.value })}
            placeholder="timeout"
          />
          <Checkbox
            label="Case sensitive"
            checked={matcher.caseSensitive}
            onChange={(e) => onChange({ ...matcher, caseSensitive: e.target.checked })}
          />
        </>
      )}
      {matcher.type === "regex" && (
        <>
          <TextInput
            aria-label="Pattern"
            className={num}
            value={matcher.pattern}
            onChange={(e) => onChange({ ...matcher, pattern: e.target.value })}
            placeholder="5\\d\\d"
          />
          <TextInput
            aria-label="Flags"
            className="!w-16"
            value={matcher.flags}
            onChange={(e) => onChange({ ...matcher, flags: e.target.value })}
            placeholder="i"
          />
        </>
      )}
      {matcher.type === "exceptionType" && (
        <>
          <TextInput
            aria-label="Exception type name"
            className={num}
            value={matcher.value}
            onChange={(e) => onChange({ ...matcher, value: e.target.value })}
            placeholder="HttpRequestException"
          />
          <Checkbox
            label="Include inner"
            checked={matcher.includeInner}
            onChange={(e) => onChange({ ...matcher, includeInner: e.target.checked })}
          />
        </>
      )}
      {matcher.type === "jsonPath" && (
        <>
          <TextInput
            aria-label="JSON path"
            className="!w-36 font-mono"
            value={matcher.path}
            onChange={(e) => onChange({ ...matcher, path: e.target.value })}
            placeholder="$.status"
          />
          <Select
            aria-label="Operator"
            value={matcher.op}
            onChange={(e) => onChange({ ...matcher, op: e.target.value as typeof matcher.op })}
            className="!w-32"
            options={[
              { value: "Eq", label: "equals" },
              { value: "Neq", label: "not equals" },
              { value: "Contains", label: "contains" },
              { value: "Exists", label: "exists" },
              { value: "NotExists", label: "doesn't exist" },
            ]}
          />
          {matcher.op !== "Exists" && matcher.op !== "NotExists" && (
            <TextInput
              aria-label="Comparison value"
              className={num}
              value={matcher.value ?? ""}
              onChange={(e) => onChange({ ...matcher, value: e.target.value })}
              placeholder="REJECTED"
            />
          )}
        </>
      )}
      <button
        onClick={onRemove}
        aria-label="Remove condition"
        className="ml-auto rounded-md p-1.5 text-ink-500 hover:bg-danger-50 hover:text-danger-700"
      >
        <Trash2 className="size-3.5" />
      </button>
    </div>
  );
}

/** Everything the group's page edits, kept as the form holds it until it is saved. */
interface GroupDraft {
  name: string;
  priority: number;
  enabled: boolean;
  appliesTo: RetryResultType[];
  action: RetryGroup["action"];
  matchers: RetryMatcher[];
  budget: { maxAttemptsPerError: number; delay: RetryDelay; maxAttemptsTotal: number };
  noTotal: boolean;
  notes: string;
  alert: RetryAlertConfig;
}

const draftOf = (group: RetryGroup | undefined): GroupDraft => ({
  name: group?.name ?? "",
  priority: group?.priority ?? 10,
  enabled: group?.enabled ?? true,
  appliesTo: group?.appliesTo ?? ["Error"],
  action: group?.action ?? "Allow",
  matchers: group?.matchers ?? [],
  budget: {
    ...(group?.budget ?? { maxAttemptsPerError: 3, delay: defaultDelayFor("exponential") }),
    // Kept while "No limit" is ticked, so unticking it brings back a number rather than a blank.
    maxAttemptsTotal: group?.budget?.maxAttemptsTotal ?? 10,
  },
  noTotal: group?.budget != null && group.budget.maxAttemptsTotal == null,
  notes: group?.notes ?? "",
  alert: { alertMode: group?.alertMode ?? "Inherit", alertChannelId: group?.alertChannelId ?? null },
});

type GroupField = "name" | "appliesTo" | "conditions" | "alert";

/** Why the server would refuse this group, caught here so the save isn't a round trip to find out. */
const needsOf = (d: GroupDraft | null, isNew: boolean): FormNeed<GroupField>[] =>
  d === null
    ? []
    : [
        {
          field: "name",
          failing: !d.name.trim(),
          needs: "a name",
          message: "Give the group a name, e.g. Timeouts.",
          // A new group's name is a field of the form; a saved one's is edited in the heading.
          focus: isNew ? "rg-name" : "rg-name-problem",
        },
        {
          field: "appliesTo",
          failing: d.appliesTo.length === 0,
          needs: "at least one kind of failure it applies to",
          message: "Tick errors, bad results, or both.",
          focus: "rg-applies-problem",
        },
        // Groups stored without conditions - from before the rule existed - still match every failure,
        // which is why the policy's table can describe one that way while this refuses to make another.
        {
          field: "conditions",
          failing: d.matchers.length === 0,
          needs: "at least one condition",
          message: "Add a condition, e.g. Error contains timeout.",
          focus: "rg-conditions-problem",
        },
        {
          field: "alert",
          failing: d.action === "Allow" && !d.noTotal && d.alert.alertMode === "Send" && d.alert.alertChannelId === null,
          needs: "the channel the budget-exhausted alert goes through, or Inherit",
          message: "Pick the channel the alert goes through, or Inherit.",
          focus: "rg-alert-problem",
        },
      ];

const groupOf = (d: GroupDraft, id: string): RetryGroup => ({
  id,
  name: d.name.trim(),
  priority: d.priority,
  enabled: d.enabled,
  appliesTo: d.appliesTo,
  matchers: d.matchers,
  action: d.action,
  budget: d.action === "Allow" ? { ...d.budget, maxAttemptsTotal: d.noTotal ? null : d.budget.maxAttemptsTotal } : undefined,
  notes: d.notes.trim() || undefined,
  // A group that blocks, or has no total, can never exhaust a budget and so never alerts. Saving
  // routing for it would leave a setting on screen that cannot ever fire.
  ...(d.action === "Allow" && !d.noTotal ? d.alert : { alertMode: "Inherit" as const, alertChannelId: null }),
});

/**
 * One group of a retry policy, on its own page. Saving writes the policy straight away, with this
 * group added or replaced in the policy as it is now - not as the policy page last saw it - so a
 * change made to another group meanwhile is kept.
 */
export function RetryGroupPage({ isNew = false }: { isNew?: boolean }) {
  const { id = "", groupId = "" } = useParams();
  const policyId = Number(id);
  const [params] = useSearchParams();
  const back = safeReturn(params.get("return")) ?? `/retry-policies/${policyId}`;
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("retry-policies.edit");
  const channelNames = useNotificationChannelNames();
  // Back from making a channel for this group's alert: the group as it was left, sending there.
  const detour = useDetour<GroupDraft>();
  const returning = useRef({ kept: detour.kept, pickedChannel: detour.pickedChannel });

  const policy = useQuery({
    queryKey: keys.retryPolicies.detail(policyId),
    queryFn: () => api.getRetryPolicy(policyId),
    retry: false,
  });
  const stored = isNew ? undefined : policy.data?.groups.find((g) => g.id === groupId);

  const [draft, setDraft] = useState<GroupDraft | null>(null);
  const [removing, setRemoving] = useState(false);
  const check = useFormCheck(needsOf(draft, isNew));

  useEffect(() => {
    if (draft !== null || !policy.data || (!isNew && !stored)) return;
    const { kept, pickedChannel } = returning.current;
    returning.current = { kept: null, pickedChannel: null };
    const next = kept ?? draftOf(stored);
    setDraft(pickedChannel !== null ? { ...next, alert: { alertMode: "Send", alertChannelId: pickedChannel } } : next);
  }, [draft, policy.data, stored, isNew]);

  // A new group counts as unsaved once anything is set. Saving, Cancel and Remove deal with the
  // changes themselves, and making a channel takes the draft along, so those go through `leave`.
  const changed = draft !== null && JSON.stringify(draft) !== JSON.stringify(draftOf(stored));
  const { leave, dialog: leaveDialog } = useLeaveGuard(changed);
  usePageTitle(isNew ? null : stored?.name);
  useCrumbTitle("retryPolicy", policy.data?.name);

  /** Writes the policy with `change` applied to its groups as they are on the server now. */
  const write = useMutation({
    mutationFn: async (change: (groups: RetryGroup[]) => RetryGroup[]) => {
      const fresh = await api.getRetryPolicy(policyId);
      await api.updateRetryPolicy(policyId, {
        name: fresh.name,
        groups: change(fresh.groups),
        alertChannelId: fresh.alertChannelId,
      });
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: keys.retryPolicies.all });
      // Editing a group can change which budgets exist, so the usage report is stale too.
      void queryClient.invalidateQueries({ queryKey: keys.retryUsage.all });
      leave(() => navigate(back, { replace: true }));
    },
  });

  if (policy.isPending) return <LoadingBlock label="Loading retry policy…" />;
  if (policy.isError || (!isNew && !stored))
    return (
      <EmptyState title={policy.isError ? "This retry policy no longer exists" : "This group no longer exists"}>
        <Link to={policy.isError ? "/retry-policies" : `/retry-policies/${policyId}`} className="font-medium link">
          Back to {policy.isError ? "retry policies" : policy.data?.name}
        </Link>
      </EmptyState>
    );
  if (draft === null) return <LoadingBlock label="Loading group…" />;

  const p = policy.data;
  const update = (patch: Partial<GroupDraft>) => setDraft((d) => ({ ...d!, ...patch }));
  const dirty = isNew || changed;

  const save = () => check.submit(() => {
    const group = groupOf(draft, isNew ? crypto.randomUUID() : groupId);
    write.mutate((groups) => {
      if (isNew) return [...groups, group];
      // Removed meanwhile, in another tab or by someone else: saving would bring it back without
      // the spent budgets and alert routes it had.
      if (!groups.some((g) => g.id === group.id))
        throw new Error("This group was removed from the policy meanwhile. Open the policy to see it as it is now.");
      return groups.map((g) => (g.id === group.id ? group : g));
    });
  });

  const toggleAppliesTo = (t: RetryResultType) =>
    update({ appliesTo: draft.appliesTo.includes(t) ? draft.appliesTo.filter((x) => x !== t) : [...draft.appliesTo, t] });

  const setDelay = (patch: Partial<RetryDelay> | { type: RetryDelay["type"] }) =>
    update({
      budget: {
        ...draft.budget,
        delay:
          "type" in patch && patch.type !== draft.budget.delay.type
            ? defaultDelayFor(patch.type as RetryDelay["type"])
            : ({ ...draft.budget.delay, ...patch } as RetryDelay),
      },
    });

  const budget = draft.budget;
  const delay = budget.delay;
  const canAlert = draft.action === "Allow" && !draft.noTotal;

  return (
    <div className="pb-10">
      <BackLink to={back} label={p.name} />

      <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">
            {/* A new group's name is asked for in the form, labelled; an empty input standing in
                for the heading read as a grey title, not as something to fill in. */}
            {isNew ? (
              draft.name.trim() || "New retry group"
            ) : (
              <EditableTitle
                value={draft.name}
                onChange={(name) => update({ name })}
                disabled={!canEdit}
                placeholder="Group name, e.g. Timeouts"
              />
            )}
          </h1>
          {!isNew && <FieldProblem id="rg-name-problem">{check.errors.name}</FieldProblem>}
          <p className="mt-1 text-sm text-ink-500">
            A group in <span className="font-medium text-ink-700">{p.name}</span>. Groups are checked top to bottom
            by priority - the first match decides.
          </p>
        </div>
        <MoreActionsMenu
          actions={[
            {
              label: "Remove group",
              icon: <Trash2 className="size-4" />,
              danger: true,
              onSelect: () => setRemoving(true),
              hidden: isNew || !canEdit,
            },
          ]}
        />
      </div>

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_360px]">
        <div className="min-w-0 space-y-5">
          <Panel title="What it catches" description="Any one condition may match; at least one is needed.">
            <div className="space-y-4">
              {isNew && (
                <div className="max-w-sm">
                  <Field label="Group name" htmlFor="rg-name" error={check.errors.name}>
                    <TextInput
                      id="rg-name"
                      autoFocus
                      value={draft.name}
                      disabled={!canEdit}
                      placeholder="e.g. Timeouts"
                      onChange={(e) => update({ name: e.target.value })}
                    />
                  </Field>
                </div>
              )}
              <div className="flex flex-wrap items-end gap-x-6 gap-y-3">
                <div className="w-32">
                  <Field label="Priority" htmlFor="rg-priority" hint="Lower runs first.">
                    <TextInput
                      id="rg-priority"
                      type="number"
                      value={draft.priority}
                      disabled={!canEdit}
                      onChange={(e) => update({ priority: Number(e.target.value) })}
                    />
                  </Field>
                </div>
                <div className="flex flex-wrap items-center gap-x-6 gap-y-2 pb-2">
                  <Checkbox
                    label="Enabled"
                    checked={draft.enabled}
                    disabled={!canEdit}
                    onChange={(e) => update({ enabled: e.target.checked })}
                  />
                  <Checkbox
                    label="Applies to errors"
                    checked={draft.appliesTo.includes("Error")}
                    disabled={!canEdit}
                    onChange={() => toggleAppliesTo("Error")}
                  />
                  <Checkbox
                    label="Applies to bad results"
                    description="Delivered, but the reply says it failed."
                    checked={draft.appliesTo.includes("BadResult")}
                    disabled={!canEdit}
                    onChange={() => toggleAppliesTo("BadResult")}
                  />
                </div>
              </div>
              <FieldProblem id="rg-applies-problem">{check.errors.appliesTo}</FieldProblem>

              <fieldset disabled={!canEdit} className="space-y-2">
                <legend className="sr-only">Conditions</legend>
                {draft.matchers.map((m, i) => (
                  <MatcherRow
                    key={i}
                    matcher={m}
                    onChange={(next) => update({ matchers: draft.matchers.map((x, xi) => (xi === i ? next : x)) })}
                    onRemove={() => update({ matchers: draft.matchers.filter((_, xi) => xi !== i) })}
                  />
                ))}
                {draft.matchers.length === 0 &&
                  (check.errors.conditions ? (
                    <FieldProblem id="rg-conditions-problem">{check.errors.conditions}</FieldProblem>
                  ) : (
                    <p className="text-[13px] text-ink-500">No conditions yet - add the first one.</p>
                  ))}
                <Button size="sm" onClick={() => update({ matchers: [...draft.matchers, DEFAULT_MATCHER] })}>
                  <Plus className="size-3.5" /> Add condition
                </Button>
              </fieldset>
            </div>
          </Panel>

          <Panel title="Then" description="Retry within a budget, or stop at once.">
            <fieldset disabled={!canEdit} className="space-y-4">
              <Field label="Action" htmlFor="rg-action">
                <Select
                  id="rg-action"
                  value={draft.action}
                  onChange={(e) => update({ action: e.target.value as RetryGroup["action"] })}
                  options={[
                    { value: "Allow", label: "Retry within a budget" },
                    { value: "Block", label: "Don't retry (stop immediately)" },
                  ]}
                />
              </Field>

              {draft.action === "Allow" && (
                <div className="space-y-3 rounded-xl bg-ink-50 p-4">
                  <div className="grid gap-3 sm:grid-cols-2">
                    <Field label="Retries per message" htmlFor="rg-per" hint="How often one failed message is tried again.">
                      <TextInput
                        id="rg-per"
                        type="number"
                        min={1}
                        value={budget.maxAttemptsPerError}
                        onChange={(e) => update({ budget: { ...budget, maxAttemptsPerError: Number(e.target.value) } })}
                      />
                    </Field>
                    <Field
                      label="Total budget"
                      htmlFor="rg-total"
                      hint={
                        draft.noTotal
                          ? "No limit across messages - each message still stops after its own retries. With nothing to run out, there is no alert."
                          : "Shared by every message this group catches, per subscription. When it runs out the group stops retrying until the budget is reset - or the subscription succeeds."
                      }
                    >
                      <div className="flex items-center gap-4">
                        <TextInput
                          id="rg-total"
                          type="number"
                          min={1}
                          disabled={draft.noTotal}
                          placeholder={draft.noTotal ? "No limit" : undefined}
                          value={draft.noTotal ? "" : budget.maxAttemptsTotal}
                          onChange={(e) => update({ budget: { ...budget, maxAttemptsTotal: Number(e.target.value) } })}
                        />
                        <span className="shrink-0" title="Don't cap retries across messages; only the per-message limit applies.">
                          <Checkbox
                            label="No limit"
                            checked={draft.noTotal}
                            onChange={(e) => update({ noTotal: e.target.checked })}
                          />
                        </span>
                      </div>
                    </Field>
                  </div>
                  <Field label="Delay between attempts" htmlFor="rg-delay">
                    <Select
                      id="rg-delay"
                      value={delay.type}
                      onChange={(e) => setDelay({ type: e.target.value as RetryDelay["type"] })}
                      options={[
                        { value: "fixed", label: "Fixed" },
                        { value: "linear", label: "Growing (linear)" },
                        { value: "exponential", label: "Doubling (exponential)" },
                      ]}
                    />
                  </Field>
                  <div className="grid gap-3 sm:grid-cols-3">
                    {delay.type === "fixed" && (
                      <Field label="Delay (seconds)" htmlFor="rg-d1">
                        <TextInput id="rg-d1" type="number" min={0} value={delay.delaySeconds} onChange={(e) => setDelay({ delaySeconds: Number(e.target.value) })} />
                      </Field>
                    )}
                    {delay.type === "linear" && (
                      <>
                        <Field label="First delay (seconds)" htmlFor="rg-d1">
                          <TextInput id="rg-d1" type="number" min={0} value={delay.initialSeconds} onChange={(e) => setDelay({ initialSeconds: Number(e.target.value) })} />
                        </Field>
                        <Field label="Adds each attempt (seconds)" htmlFor="rg-d2">
                          <TextInput id="rg-d2" type="number" min={0} value={delay.incrementSeconds} onChange={(e) => setDelay({ incrementSeconds: Number(e.target.value) })} />
                        </Field>
                      </>
                    )}
                    {delay.type === "exponential" && (
                      <>
                        <Field label="First delay (seconds)" htmlFor="rg-d1">
                          <TextInput id="rg-d1" type="number" min={0} value={delay.initialSeconds} onChange={(e) => setDelay({ initialSeconds: Number(e.target.value) })} />
                        </Field>
                        <Field label="Multiplier" htmlFor="rg-d2">
                          <TextInput id="rg-d2" type="number" min={1} step="0.1" value={delay.multiplier} onChange={(e) => setDelay({ multiplier: Number(e.target.value) })} />
                        </Field>
                        <Field label="Max delay (seconds)" htmlFor="rg-d3">
                          <TextInput id="rg-d3" type="number" min={0} value={delay.maxSeconds} onChange={(e) => setDelay({ maxSeconds: Number(e.target.value) })} />
                        </Field>
                      </>
                    )}
                  </div>
                </div>
              )}
            </fieldset>
          </Panel>
        </div>

        <div className="min-w-0 space-y-5">
          <Panel title="When the budget runs out" description="The alert sent once this group stops retrying.">
            {canAlert && check.errors.alert && (
              <div className="mb-3">
                <FieldProblem id="rg-alert-problem">{check.errors.alert}</FieldProblem>
              </div>
            )}
            {canAlert ? (
              <AlertRouting
                value={draft.alert}
                onChange={(alert) => update({ alert })}
                disabled={!canEdit}
                onNewChannel={() => leave(() => detour.newChannel(draft, "group"))}
                onEditChannel={(channelId) => leave(() => detour.openChannel(draft, channelId))}
                inherited={
                  p.alertChannelId !== null ? (
                    <>
                      Sends through the policy default,{" "}
                      <span className="font-medium text-ink-700">
                        {channelNames.data?.find((c) => c.id === p.alertChannelId)?.name ?? "…"}
                      </span>
                      .
                    </>
                  ) : (
                    "The policy sends no alert, so nothing is sent - unless a single subscription overrides it."
                  )
                }
              />
            ) : (
              <p className="text-[13px] text-ink-500">
                {draft.action === "Block"
                  ? "It blocks retries outright, so there is no budget to run out and nothing to alert on."
                  : "With no total limit there is nothing to run out, so nothing to alert on."}
              </p>
            )}
          </Panel>

          <Panel title="Notes" description="Optional - why this group exists.">
            <TextInput
              aria-label="Notes"
              value={draft.notes}
              disabled={!canEdit}
              onChange={(e) => update({ notes: e.target.value })}
            />
          </Panel>
        </div>
      </div>

      {canEdit && (
        <>
          <div className="mt-5 flex flex-wrap items-center justify-end gap-3 rounded-xl border border-ink-200 bg-white px-4 py-3">
            <p className="mr-auto text-[13px] text-ink-500">Saving writes the policy straight away.</p>
            <StillNeeds missing={check.missing} />
            <Button onClick={() => leave(() => navigate(back, { replace: true }))}>Cancel</Button>
            <Button variant="primary" busy={write.isPending} disabled={!dirty} onClick={save}>
              {isNew ? "Add group" : "Save group"}
            </Button>
          </div>
          <FormError>{write.error?.message}</FormError>
        </>
      )}

      {leaveDialog}
      {removing && (
        <DeleteDialog
          title={`Remove ${stored?.name}?`}
          name={stored?.name ?? ""}
          // Every subscription on the policy is checked against this group.
          impact={[counted(p.subscriptions.length, "subscription on this policy", "subscriptions on this policy")]}
          body={
            <p>
              Failures it caught fall through to the groups below it - or, with none left to match, aren't
              retried. Its spent budgets go with it.
            </p>
          }
          confirmLabel="Remove group"
          onConfirm={async () => {
            await write.mutateAsync((groups) => groups.filter((g) => g.id !== groupId));
          }}
          onClose={() => setRemoving(false)}
        />
      )}
    </div>
  );
}
