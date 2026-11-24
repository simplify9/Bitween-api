import { useId } from "react";
import type { Role } from "../../api";

/**
 * The one role an account holds, picked from a list: built-in roles first, then custom ones.
 *
 * One, because roles used to add up and nobody could read that off the screen - Administrator
 * ticked beside Member meant nothing more. A custom role is a whole set instead, made by
 * duplicating the nearest built-in one. An account still holding several from before shows them
 * and asks for one; they keep adding up until it is chosen.
 */
export function RolePicker({
  roles,
  value,
  onChange,
  legend = "Role",
  hint,
}: {
  /** Already narrowed to the account's side: partner roles for a partner's person, ours otherwise. */
  roles: Role[];
  /** The role ids the account holds now; more than one only for an account from before. */
  value: string[];
  onChange: (roleId: string) => void;
  legend?: string;
  hint?: string;
}) {
  const name = useId();
  const chosen = value.length === 1 ? value[0] : null;
  const builtIn = roles.filter((r) => r.isSystem);
  const custom = roles.filter((r) => !r.isSystem);
  const held = roles.filter((r) => value.includes(r.id)).map((r) => r.name);

  const option = (role: Role) => (
    <label key={role.id} className="flex cursor-pointer items-start gap-2.5">
      <input
        type="radio"
        name={name}
        className="mt-1 size-4 shrink-0 accent-crimson-600"
        checked={chosen === role.id}
        onChange={() => onChange(role.id)}
      />
      <span>
        <span className="text-sm font-medium text-ink-900">{role.name}</span>
        {role.description && <span className="block text-[13px] text-ink-500">{role.description}</span>}
      </span>
    </label>
  );

  return (
    <fieldset>
      <legend className="mb-2 block text-[13px] font-medium text-ink-700">{legend}</legend>
      {value.length > 1 && (
        <p className="mb-2 rounded-lg bg-warn-50 px-3 py-2 text-[13px] text-ink-700">
          Holds {held.join(" and ")} from before, which add up. Choose one role to replace them.
        </p>
      )}
      <div className="max-h-64 space-y-2.5 overflow-y-auto rounded-lg border border-ink-200 p-3">
        {builtIn.map(option)}
        {custom.length > 0 && (
          <div
            className="pt-1 text-[11px] font-semibold tracking-wide text-ink-500 uppercase"
            title="Roles made here, each a whole set of permissions. To give someone a little more than a built-in role, duplicate it under Roles and add what they need."
          >
            Custom
          </div>
        )}
        {custom.map(option)}
        {roles.length === 0 && <p className="text-[13px] text-ink-500">No roles yet.</p>}
      </div>
      {hint && <p className="mt-1.5 text-[13px] text-ink-500">{hint}</p>}
    </fieldset>
  );
}
