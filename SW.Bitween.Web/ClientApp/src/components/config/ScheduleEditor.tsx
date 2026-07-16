import { useState } from "react";
import { CalendarClock, Pencil, Plus, Trash2 } from "lucide-react";
import type { Recurrence, Schedule } from "../../api";
import { WEEKDAYS, scheduleSummary } from "../../lib/schedules";
import { Button } from "../ui/basics";
import { Checkbox, Field, Select, TextInput } from "../ui/forms";
import { Dialog } from "../ui/overlays";

const DEFAULT_SCHEDULE: Schedule = { recurrence: "Daily", days: 0, hours: 2, minutes: 0, backwards: false };

function ScheduleDialog({
  initial,
  onSubmit,
  onClose,
}: {
  initial?: Schedule;
  onSubmit: (schedule: Schedule) => void;
  onClose: () => void;
}) {
  const [draft, setDraft] = useState<Schedule>(initial ?? DEFAULT_SCHEDULE);
  const set = <K extends keyof Schedule>(key: K, value: Schedule[K]) =>
    setDraft((d) => ({ ...d, [key]: value }));

  const setRecurrence = (recurrence: Recurrence) =>
    setDraft((d) => ({
      ...d,
      recurrence,
      // keep days meaningful per recurrence
      days: recurrence === "Monthly" ? Math.min(Math.max(d.days, 1), 27) : recurrence === "Weekly" ? Math.min(d.days, 6) : 0,
    }));

  const submit = () => {
    onSubmit(draft);
    onClose();
  };

  return (
    <Dialog title={initial ? "Edit schedule" : "Add schedule"} onClose={onClose}>
      <div className="space-y-4">
        <Field label="Runs" htmlFor="sc-rec">
          <Select
            id="sc-rec"
            value={draft.recurrence}
            onChange={(e) => setRecurrence(e.target.value as Recurrence)}
            options={[
              { value: "Hourly", label: "Every hour" },
              { value: "Daily", label: "Every day" },
              { value: "Weekly", label: "Every week" },
              { value: "Monthly", label: "Every month" },
            ]}
          />
        </Field>

        <div className="grid gap-3 sm:grid-cols-2">
          {draft.recurrence === "Weekly" && (
            <Field label="On" htmlFor="sc-day">
              <Select
                id="sc-day"
                value={String(draft.days)}
                onChange={(e) => set("days", Number(e.target.value))}
                options={WEEKDAYS.map((d, i) => ({ value: String(i), label: d }))}
              />
            </Field>
          )}
          {draft.recurrence === "Monthly" && (
            <Field label="On day" htmlFor="sc-dom" hint="1–27, so it exists in every month.">
              <TextInput
                id="sc-dom"
                type="number"
                min={1}
                max={27}
                value={draft.days}
                onChange={(e) => set("days", Math.min(Math.max(Number(e.target.value), 1), 27))}
              />
            </Field>
          )}
          {draft.recurrence !== "Hourly" && (
            <Field label="At hour" htmlFor="sc-h">
              <TextInput
                id="sc-h"
                type="number"
                min={0}
                max={23}
                value={draft.hours}
                onChange={(e) => set("hours", Math.min(Math.max(Number(e.target.value), 0), 23))}
              />
            </Field>
          )}
          <Field label="At minute" htmlFor="sc-m">
            <TextInput
              id="sc-m"
              type="number"
              min={0}
              max={59}
              value={draft.minutes}
              onChange={(e) => set("minutes", Math.min(Math.max(Number(e.target.value), 0), 59))}
            />
          </Field>
        </div>

        <Checkbox
          label="Count from the end of the period"
          description='e.g. "2 hours before the end of the day" instead of "2 hours after it starts".'
          checked={draft.backwards}
          onChange={(e) => set("backwards", e.target.checked)}
        />

        <p className="rounded-lg bg-ink-50 px-3 py-2 text-[13px] text-ink-600">{scheduleSummary(draft)}</p>

        <div className="flex justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button variant="primary" onClick={submit}>
            {initial ? "Update schedule" : "Add schedule"}
          </Button>
        </div>
      </div>
    </Dialog>
  );
}

/** Controlled list of run schedules for receivers. */
export function ScheduleEditor({
  schedules,
  onChange,
  disabled,
}: {
  schedules: Schedule[];
  onChange: (schedules: Schedule[]) => void;
  disabled: boolean;
}) {
  const [editing, setEditing] = useState<number | "new" | null>(null);

  return (
    <div>
      {schedules.length === 0 ? (
        <p className="pb-2 text-sm text-ink-500">
          No schedule yet — this receiver never runs on its own.
        </p>
      ) : (
        <ul className="space-y-1.5">
          {schedules.map((s, i) => (
            <li key={i} className="flex items-center gap-2.5 text-sm">
              <CalendarClock className="size-3.5 shrink-0 text-ink-300" aria-hidden />
              <span className="min-w-0 flex-1 truncate text-ink-800">{scheduleSummary(s)}</span>
              {!disabled && (
                <span className="flex gap-1">
                  <button
                    onClick={() => setEditing(i)}
                    aria-label={`Edit schedule ${i + 1}`}
                    className="rounded-md p-1.5 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
                  >
                    <Pencil className="size-3.5" />
                  </button>
                  <button
                    onClick={() => onChange(schedules.filter((_, x) => x !== i))}
                    aria-label={`Remove schedule ${i + 1}`}
                    className="rounded-md p-1.5 text-ink-400 hover:bg-crimson-50 hover:text-crimson-700"
                  >
                    <Trash2 className="size-3.5" />
                  </button>
                </span>
              )}
            </li>
          ))}
        </ul>
      )}
      {!disabled && (
        <Button size="sm" className="mt-2" onClick={() => setEditing("new")}>
          <Plus className="size-3.5" /> Add schedule
        </Button>
      )}
      {editing !== null && (
        <ScheduleDialog
          initial={editing === "new" ? undefined : schedules[editing]}
          onSubmit={(schedule) =>
            onChange(
              editing === "new" ? [...schedules, schedule] : schedules.map((s, i) => (i === editing ? schedule : s)),
            )
          }
          onClose={() => setEditing(null)}
        />
      )}
    </div>
  );
}
