import { useState } from "react";
import type { ExpectTraffic } from "../../../api";
import { ProblemText } from "../../../components/ui/basics";
import { Panel } from "../../../components/ui/Panel";
import { Checkbox, Field, TextInput } from "../../../components/ui/forms";

const DAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

/** What switching it on starts from: hourly, every day, all day, UTC. */
const DEFAULT: ExpectTraffic = { everyMinutes: 60, days: null, from: null, to: null, timeZone: null };

/** The server's own floor; below it the check would run more often than it can tell. */
const MIN_MINUTES = 5;

// Read once: the list is several hundred names and never changes in a session.
const TIME_ZONES: string[] = typeof Intl.supportedValuesOf === "function" ? Intl.supportedValuesOf("timeZone") : [];

/**
 * What the server would turn down, said before Save so it is said beside the field. The server
 * checks the same and more (whether a time zone exists), so this only has to catch the obvious.
 */
function problems(e: ExpectTraffic): { every?: string; window?: string } {
  const out: { every?: string; window?: string } = {};
  if (!Number.isInteger(e.everyMinutes) || e.everyMinutes < MIN_MINUTES)
    out.every = `At least ${MIN_MINUTES} minutes.`;
  if ((e.from === null) !== (e.to === null)) out.window = "Give both From and To, or neither.";
  // "HH:mm" compares correctly as text.
  else if (e.from !== null && e.to !== null && e.to <= e.from)
    out.window = "To must be later than From; a window cannot run past midnight.";
  return out;
}

/**
 * Whether this subscription should see traffic regularly, so the server can say when it goes
 * quiet. Part of the page's draft: it saves with everything else on the save bar.
 */
export function ExpectTrafficPanel({
  value,
  onChange,
  disabled,
}: {
  value: ExpectTraffic | null;
  onChange: (v: ExpectTraffic | null) => void;
  disabled: boolean;
}) {
  const [zoneFocused, setZoneFocused] = useState(false);
  const on = value !== null;
  const issues = value ? problems(value) : {};
  const patch = (p: Partial<ExpectTraffic>) => value && onChange({ ...value, ...p });

  const toggleDay = (day: string) => {
    if (!value) return;
    const days = value.days ?? [];
    const next = days.includes(day) ? days.filter((d) => d !== day) : [...days, day];
    // Kept in week order, and none at all stored as null so it reads back unchanged.
    patch({ days: next.length ? DAYS.filter((d) => next.includes(d)) : null });
  };

  return (
    <Panel title="Expect traffic" description="Flag this subscription when nothing arrives for longer than expected.">
      <div className="space-y-4">
        <Checkbox
          label="Expect traffic regularly"
          checked={on}
          disabled={disabled}
          onChange={(e) => onChange(e.target.checked ? DEFAULT : null)}
        />
        {value && (
          <>
            <Field label="Every" error={issues.every}>
              <div className="flex items-center gap-2 text-[13px] text-ink-700">
                <TextInput
                  aria-label="Expect traffic every this many minutes"
                  type="number"
                  min={MIN_MINUTES}
                  inputMode="numeric"
                  value={Number.isFinite(value.everyMinutes) && value.everyMinutes > 0 ? value.everyMinutes : ""}
                  disabled={disabled}
                  // Kept as typed, not clamped: clamping would turn the "1" of "15" into 5.
                  onChange={(e) => {
                    const n = e.target.valueAsNumber;
                    patch({ everyMinutes: Number.isFinite(n) ? Math.floor(n) : 0 });
                  }}
                  className="w-24!"
                />
                <span>minutes</span>
              </div>
            </Field>

            <div className="space-y-1.5">
              <p className="text-[13px] font-medium text-ink-700">On</p>
              <div className="flex flex-wrap gap-1.5" role="group" aria-label="Days">
                {DAYS.map((d) => {
                  const pressed = value.days?.includes(d) ?? false;
                  return (
                    <button
                      key={d}
                      type="button"
                      aria-pressed={pressed}
                      disabled={disabled}
                      onClick={() => toggleDay(d)}
                      className={`rounded-lg border px-2.5 py-1 text-[13px] disabled:opacity-60 ${
                        pressed
                          ? "border-crimson-600 bg-crimson-50 text-crimson-700"
                          : "border-ink-200 bg-white text-ink-600 hover:bg-ink-50"
                      }`}
                    >
                      {d}
                    </button>
                  );
                })}
              </div>
              {!value.days?.length && <p className="text-[13px] text-ink-500">None picked: every day.</p>}
            </div>

            <div className="space-y-1.5">
              <div className="flex flex-wrap items-end gap-3">
                <label className="text-[13px] font-medium text-ink-700">
                  From
                  <TextInput
                    type="time"
                    value={value.from ?? ""}
                    disabled={disabled}
                    aria-invalid={issues.window ? true : undefined}
                    onChange={(e) => patch({ from: e.target.value || null })}
                    className="mt-1.5 block w-32!"
                  />
                </label>
                <label className="text-[13px] font-medium text-ink-700">
                  To
                  <TextInput
                    type="time"
                    value={value.to ?? ""}
                    disabled={disabled}
                    aria-invalid={issues.window ? true : undefined}
                    onChange={(e) => patch({ to: e.target.value || null })}
                    className="mt-1.5 block w-32!"
                  />
                </label>
                <label className="text-[13px] font-medium text-ink-700">
                  Time zone
                  <TextInput
                    list="expect-traffic-zones"
                    placeholder="UTC"
                    value={value.timeZone ?? ""}
                    disabled={disabled}
                    onFocus={() => setZoneFocused(true)}
                    onBlur={() => setZoneFocused(false)}
                    onChange={(e) => patch({ timeZone: e.target.value.trim() || null })}
                    className="mt-1.5 block w-56!"
                  />
                  {/* Filled only while focused: several hundred options in the DOM for a field
                      nobody is using slow every render of the page. */}
                  <datalist id="expect-traffic-zones">
                    {zoneFocused && TIME_ZONES.map((z) => <option key={z} value={z} />)}
                  </datalist>
                </label>
              </div>
              {issues.window ? (
                <p role="alert">
                  <ProblemText>{issues.window}</ProblemText>
                </p>
              ) : (
                <p className="text-[13px] text-ink-500">Leave From and To empty to watch all day.</p>
              )}
            </div>
          </>
        )}
      </div>
    </Panel>
  );
}
