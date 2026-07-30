import { useState, type FormEvent } from "react";
import { Plus, Trash2 } from "lucide-react";
import type { RetryDelay, RetryGroup, RetryMatcher, RetryResultType } from "../../api";
import { Button, FormError } from "../../components/ui/basics";
import { Checkbox, Field, Select, TextInput } from "../../components/ui/forms";
import { Dialog } from "../../components/ui/overlays";

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
  const num = "flex-1 min-w-24";
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
        className="ml-auto rounded-md p-1.5 text-ink-400 hover:bg-danger-50 hover:text-danger-700"
      >
        <Trash2 className="size-3.5" />
      </button>
    </div>
  );
}

export function GroupDialog({
  initial,
  onSubmit,
  onClose,
}: {
  initial?: RetryGroup;
  onSubmit: (group: RetryGroup) => void;
  onClose: () => void;
}) {
  const [name, setName] = useState(initial?.name ?? "");
  const [priority, setPriority] = useState(initial?.priority ?? 10);
  const [enabled, setEnabled] = useState(initial?.enabled ?? true);
  const [appliesTo, setAppliesTo] = useState<RetryResultType[]>(initial?.appliesTo ?? ["Error"]);
  const [action, setAction] = useState<RetryGroup["action"]>(initial?.action ?? "Allow");
  const [matchers, setMatchers] = useState<RetryMatcher[]>(initial?.matchers ?? []);
  const [budget, setBudget] = useState(
    initial?.budget ?? { maxAttemptsPerError: 3, maxAttemptsTotal: 10, delay: defaultDelayFor("exponential") },
  );
  const [notes, setNotes] = useState(initial?.notes ?? "");
  const [error, setError] = useState("");

  const toggleAppliesTo = (t: RetryResultType) =>
    setAppliesTo((prev) => (prev.includes(t) ? prev.filter((x) => x !== t) : [...prev, t]));

  const setDelay = (patch: Partial<RetryDelay> | { type: RetryDelay["type"] }) => {
    setBudget((b) => ({
      ...b,
      delay: "type" in patch && patch.type !== b.delay.type
        ? defaultDelayFor(patch.type as RetryDelay["type"])
        : ({ ...b.delay, ...patch } as RetryDelay),
    }));
  };

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (!name.trim()) return setError("Give the group a name.");
    if (appliesTo.length === 0) return setError("Pick at least one failure kind it applies to.");
    onSubmit({
      id: initial?.id ?? crypto.randomUUID(),
      name: name.trim(),
      priority,
      enabled,
      appliesTo,
      matchers,
      action,
      budget: action === "Allow" ? budget : undefined,
      notes: notes.trim() || undefined,
    });
    onClose();
  };

  const delay = budget.delay;

  return (
    <Dialog title={initial ? "Edit group" : "New group"} onClose={onClose} wide>
      <form onSubmit={submit} className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-[1fr_8rem]">
          <Field label="Name" htmlFor="rg-name">
            <TextInput id="rg-name" autoFocus value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Timeouts" />
          </Field>
          <Field label="Priority" htmlFor="rg-priority" hint="Lower runs first.">
            <TextInput
              id="rg-priority"
              type="number"
              value={priority}
              onChange={(e) => setPriority(Number(e.target.value))}
            />
          </Field>
        </div>

        <div className="flex flex-wrap items-center gap-x-6 gap-y-2">
          <Checkbox label="Enabled" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />
          <Checkbox
            label="Applies to errors"
            checked={appliesTo.includes("Error")}
            onChange={() => toggleAppliesTo("Error")}
          />
          <Checkbox
            label="Applies to bad results"
            description="Delivered, but the reply says it failed."
            checked={appliesTo.includes("BadResult")}
            onChange={() => toggleAppliesTo("BadResult")}
          />
        </div>

        <fieldset>
          <legend className="mb-1.5 block text-[13px] font-medium text-ink-700">
            Conditions <span className="font-normal text-ink-500">— any may match; none means every failure</span>
          </legend>
          <div className="space-y-2">
            {matchers.map((m, i) => (
              <MatcherRow
                key={i}
                matcher={m}
                onChange={(next) => setMatchers(matchers.map((x, xi) => (xi === i ? next : x)))}
                onRemove={() => setMatchers(matchers.filter((_, xi) => xi !== i))}
              />
            ))}
            <Button size="sm" onClick={() => setMatchers([...matchers, DEFAULT_MATCHER])}>
              <Plus className="size-3.5" /> Add condition
            </Button>
          </div>
        </fieldset>

        <Field label="Then" htmlFor="rg-action">
          <Select
            id="rg-action"
            value={action}
            onChange={(e) => setAction(e.target.value as RetryGroup["action"])}
            options={[
              { value: "Allow", label: "Retry within a budget" },
              { value: "Block", label: "Don't retry (stop immediately)" },
            ]}
          />
        </Field>

        {action === "Allow" && (
          <div className="space-y-3 rounded-xl bg-ink-50 p-4">
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label="Max attempts per error" htmlFor="rg-per">
                <TextInput
                  id="rg-per"
                  type="number"
                  min={1}
                  value={budget.maxAttemptsPerError}
                  onChange={(e) => setBudget({ ...budget, maxAttemptsPerError: Number(e.target.value) })}
                />
              </Field>
              <Field label="Max attempts total" htmlFor="rg-total">
                <TextInput
                  id="rg-total"
                  type="number"
                  min={1}
                  value={budget.maxAttemptsTotal}
                  onChange={(e) => setBudget({ ...budget, maxAttemptsTotal: Number(e.target.value) })}
                />
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

        <Field label="Notes" htmlFor="rg-notes" hint="Optional — why this group exists.">
          <TextInput id="rg-notes" value={notes} onChange={(e) => setNotes(e.target.value)} />
        </Field>

        <FormError>{error}</FormError>
        <div className="flex justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary">
            {initial ? "Update group" : "Add group"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
