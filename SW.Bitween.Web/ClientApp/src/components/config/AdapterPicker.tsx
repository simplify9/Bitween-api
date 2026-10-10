import { useState } from "react";
import { ChevronLeft } from "lucide-react";
import { type AdapterInfo, type AdapterKind } from "../../api";
import { SearchSelect } from "../ui/SearchSelect";

/** The sentinel option that opens the custom list. Not an adapter id. */
const CUSTOM = "__custom__";

/**
 * Picks one adapter, in two steps: the adapters that ship with Bitween, and —
 * behind one row — the custom ones deployed to this instance.
 *
 * The two are never listed together. Mixed, the only thing separating a
 * supported adapter from something a client uploaded was a word at the end of
 * the row, and the list grew with every deployment, so the common choice got
 * harder to find the more an instance was used.
 */
export function AdapterPicker({
  kind,
  value,
  catalog,
  loading = false,
  onChange,
  disabled = false,
  clearLabel,
  id,
  label,
}: {
  kind: AdapterKind;
  /** "" = nothing selected. */
  value: string;
  catalog: AdapterInfo[];
  /**
   * True while the catalog is still arriving. Without it an empty catalog is
   * indistinguishable from one that simply doesn't list the saved adapter, and
   * every configured slot would flash the custom list on the way in.
   */
  loading?: boolean;
  onChange: (adapterId: string) => void;
  disabled?: boolean;
  /** When set, an empty choice with this label is offered (e.g. "None"). */
  clearLabel?: string;
  id?: string;
  /**
   * What this field is called, where a visible label already names it something
   * of its own ("Deliver via"). Defaults to the kind; pass the visible wording so
   * the accessible name doesn't contradict what is on screen.
   */
  label?: string;
}) {
  const builtIn = catalog.filter((a) => a.native);
  const custom = catalog.filter((a) => !a.native);
  const known = catalog.find((a) => a.id === value);

  /*
    Which list is open follows what is *selected*, so opening a subscription that
    runs a custom adapter shows it straight away rather than an empty built-in
    list. An adapter the catalog no longer lists — unpublished, or no longer
    declaring this kind — counts as custom: it is certainly not one of ours, and
    it has to stay visible, or the page reads as unconfigured when it is not.
    State only carries the case the value can't: "custom, nothing picked yet".
  */
  const [browsingCustom, setBrowsingCustom] = useState(false);
  const showCustom = !loading && (browsingCustom || (value !== "" && !known?.native));

  const toOption = (a: AdapterInfo) => ({
    value: a.id,
    label: a.label,
    // The real id: what a subscription stores, and what someone who knows an
    // adapter by its class name will type to find it.
    code: a.id,
    hint: a.versions.length > 0 ? `v${a.versions.at(-1)}` : undefined,
  });

  const unlisted =
    value !== "" && !known ? [{ value, label: value, code: value, hint: "Not in catalog" }] : [];

  if (showCustom) {
    return (
      <div className="flex items-center gap-1.5">
        <button
          type="button"
          disabled={disabled}
          onClick={() => {
            setBrowsingCustom(false);
            // Leaving the custom list keeps nothing selected from it: the built-in
            // list is about to be shown with a custom adapter still chosen, which
            // is the one state that reads as a lie.
            if (value !== "" && !known?.native) onChange("");
          }}
          className="shrink-0 rounded-md px-1.5 py-1 text-[12px] font-medium text-ink-500 hover:bg-ink-100 hover:text-ink-800 disabled:opacity-50"
        >
          <ChevronLeft className="-ml-0.5 inline size-3.5" aria-hidden /> Built-in
        </button>
        <div className="min-w-0 flex-1">
          <SearchSelect
            id={id}
            aria-label={`Custom ${label ?? `${kind} adapter`}`}
            value={value}
            disabled={disabled}
            onChange={onChange}
            placeholder={custom.length === 0 ? "No custom adapters deployed" : "Pick a custom adapter…"}
            options={[...custom.map(toOption), ...unlisted]}
          />
        </div>
      </div>
    );
  }

  return (
    <SearchSelect
      id={id}
      aria-label={label ?? `${kind} adapter`}
      value={value}
      disabled={disabled}
      onChange={(v) => {
        if (v === CUSTOM) return setBrowsingCustom(true);
        onChange(v);
      }}
      placeholder={`Pick a ${kind}…`}
      clearLabel={clearLabel}
      options={[
        ...builtIn.map(toOption),
        {
          value: CUSTOM,
          label: "Custom adapter…",
          hint: custom.length > 0 ? `${custom.length}` : "none deployed",
        },
      ]}
    />
  );
}
