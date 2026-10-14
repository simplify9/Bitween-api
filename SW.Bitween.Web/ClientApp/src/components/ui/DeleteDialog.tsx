import { useId, useState, type ReactNode } from "react";
import { ConfirmDialog } from "./overlays";
import { TextInput } from "./forms";

export interface DeleteImpact {
  /** What is counted, already pluralised, e.g. "6 subscriptions". */
  label: string;
  count: number;
}

/**
 * Delete confirmation that weighs what is at stake.
 *
 * Something nothing uses keeps the one-click confirm. Something whose delete
 * takes other things with it lists them and asks for its name before Delete is
 * enabled: the same dialog for a gateway nobody calls and one with forty partner
 * attachments made both feel equally safe to click through.
 *
 * Two kinds of use, because they call for different answers:
 * - `impact` is what goes with it or stops working - the name is asked for.
 * - `blockers` is what the server refuses the delete over. Typing a name only to
 *   be refused would be friction for nothing, so these are listed with the way
 *   out instead, and Delete is left to the server, whose answer shows here.
 *
 * The counts come from the "Used by" data the page has already loaded, so this
 * never waits on a request of its own.
 */
export function DeleteDialog({
  title,
  name,
  body,
  impact = [],
  blockers = [],
  confirmLabel,
  onConfirm,
  onClose,
}: {
  title: string;
  /** The name the operator types to confirm, exactly as the page shows it. */
  name: string;
  /** The consequence copy, as the page wrote it before. */
  body: ReactNode;
  /** Zero counts are left out; with nothing left it is a plain confirm. */
  impact?: DeleteImpact[];
  blockers?: DeleteImpact[];
  confirmLabel: string;
  onConfirm: () => Promise<void>;
  onClose: () => void;
}) {
  const [typed, setTyped] = useState("");
  const inputId = useId();
  const held = blockers.filter((b) => b.count > 0);
  const breaks = impact.filter((i) => i.count > 0);
  const guarded = held.length === 0 && breaks.length > 0 && name.trim() !== "";

  return (
    <ConfirmDialog
      title={title}
      confirmLabel={confirmLabel}
      onConfirm={onConfirm}
      onClose={onClose}
      // Trimmed on both sides: a stray space from a copy and paste is not a different name.
      confirmDisabled={guarded && typed.trim() !== name.trim()}
      body={
        <div className="space-y-3">
          {body && <div>{body}</div>}
          {held.length > 0 && (
            <div className="rounded-lg border border-warn-300 bg-warn-100 px-3 py-2.5">
              <p className="font-medium text-ink-800">Still used by:</p>
              <ul className="mt-1 list-disc pl-5 text-ink-700">
                {held.map((b) => (
                  <li key={b.label}>{b.label}</li>
                ))}
              </ul>
              <p className="mt-1.5 text-ink-600">Deleting is refused until they are moved off it.</p>
            </div>
          )}
          {guarded && (
            <>
              <div className="rounded-lg border border-danger-200 bg-danger-50 px-3 py-2.5">
                <p className="font-medium text-danger-800">Deleting it also affects:</p>
                <ul className="mt-1 list-disc pl-5 text-danger-800">
                  {breaks.map((b) => (
                    <li key={b.label}>{b.label}</li>
                  ))}
                </ul>
              </div>
              <div>
                <label htmlFor={inputId} className="block text-ink-700">
                  Type <strong className="font-medium text-ink-900">{name}</strong> to confirm.
                </label>
                <TextInput
                  id={inputId}
                  value={typed}
                  onChange={(e) => setTyped(e.target.value)}
                  autoComplete="off"
                  spellCheck={false}
                  className="mt-1.5"
                />
              </div>
            </>
          )}
        </div>
      }
    />
  );
}

/** "1 subscription", "6 subscriptions". */
export function counted(count: number, one: string, many = `${one}s`): DeleteImpact {
  return { label: `${count} ${count === 1 ? one : many}`, count };
}
