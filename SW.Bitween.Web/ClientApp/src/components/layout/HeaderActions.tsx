import type { ReactNode } from "react";
import { Menu, MenuButton, MenuItem, MenuItems, MenuSeparator } from "@headlessui/react";
import { Ellipsis } from "lucide-react";
import { SegmentedControl } from "../ui/SegmentedControl";

/*
 * The one shape every detail page's header actions take: the running state, if
 * the thing has one, as a single control that says what the current state means,
 * then a "More actions" menu for everything that is done rarely - Delete last and
 * set apart.
 *
 * Delete used to be a red button in the header of every detail page, which made
 * removing the thing the most prominent action on a page people mostly come to
 * read or tune. Pause and Disable sat beside it as two look-alike buttons with
 * very different consequences; one control with three named states shows which
 * one is on and what that means, so the difference no longer has to be guessed
 * from a tooltip.
 */

export interface RunStateOption<T extends string> {
  value: T;
  label: string;
  /** One line on what this state means for the work, shown while it is the current one. */
  caption: string;
}

/**
 * The running state as a segmented control with a caption for the current state.
 * Choosing a state does not change anything by itself: `onChange` is expected to
 * open the page's confirm dialog, and the control keeps showing the saved state
 * until that confirm has gone through.
 */
export function RunStateControl<T extends string>({
  label,
  options,
  value,
  onChange,
  disabled = false,
  compact = false,
}: {
  /** Names the group for a screen reader, e.g. "Running state". */
  label: string;
  options: RunStateOption<T>[];
  value: T;
  onChange: (value: T) => void;
  disabled?: boolean;
  /** For a one-line toolbar: no caption line, each state explains itself on hover instead. */
  compact?: boolean;
}) {
  const current = options.find((o) => o.value === value);
  const control = (
    <SegmentedControl
      label={label}
      // Every state carries its caption as a tooltip, so the ones not chosen can be
      // read before choosing them.
      options={options.map(({ value, label, caption }) => ({ value, label, title: caption }))}
      value={value}
      // Re-choosing the current state is not a change worth a dialog.
      onChange={(next) => next !== value && onChange(next)}
      disabled={disabled}
    />
  );
  if (compact) return control;
  return (
    <div className="flex flex-col items-start gap-1 sm:items-end">
      {control}
      {current && <p className="max-w-72 text-xs text-ink-500 sm:text-right">{current.caption}</p>}
    </div>
  );
}

export interface MoreAction {
  label: string;
  icon?: ReactNode;
  onSelect: () => void;
  /** Destructive: drawn red and kept apart, after every other item. */
  danger?: boolean;
  /** Hover explanation, e.g. why the item is unavailable. */
  title?: string;
  disabled?: boolean;
  /** Leaves the item out, for an action the session has no permission for. */
  hidden?: boolean;
}

/**
 * The "···" menu for a page's rare actions. Renders nothing when every item is
 * hidden, so a read-only session doesn't get a menu that opens onto nothing.
 */
export function MoreActionsMenu({ actions }: { actions: MoreAction[] }) {
  const shown = actions.filter((a) => !a.hidden);
  if (shown.length === 0) return null;
  const plain = shown.filter((a) => !a.danger);
  const danger = shown.filter((a) => a.danger);

  const item = (a: MoreAction) => (
    <MenuItem key={a.label} disabled={a.disabled}>
      <button
        type="button"
        onClick={a.onSelect}
        title={a.title}
        className={`flex w-full items-center gap-2 rounded-lg px-2.5 py-1.5 text-left text-sm data-disabled:cursor-not-allowed data-disabled:opacity-50 ${
          a.danger
            ? "text-danger-700 data-focus:bg-danger-50"
            : "text-ink-700 data-focus:bg-ink-50"
        }`}
      >
        {a.icon}
        {a.label}
      </button>
    </MenuItem>
  );

  return (
    <Menu>
      <MenuButton
        aria-label="More actions"
        title="More actions"
        className="inline-flex h-9.5 w-9.5 shrink-0 cursor-pointer items-center justify-center rounded-lg border border-ink-200 bg-white text-ink-700 hover:border-ink-300 hover:bg-ink-50 data-open:bg-ink-50"
      >
        <Ellipsis className="size-4.5" />
      </MenuButton>
      <MenuItems
        anchor={{ to: "bottom end", gap: 6 }}
        className="z-50 min-w-52 rounded-xl border border-ink-100 bg-white p-1 shadow-lg outline-none"
      >
        {plain.map(item)}
        {plain.length > 0 && danger.length > 0 && <MenuSeparator className="my-1 h-px bg-ink-100" />}
        {danger.map(item)}
      </MenuItems>
    </Menu>
  );
}
