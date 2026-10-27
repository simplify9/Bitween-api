import { useEffect, useId, useMemo, useRef, useState, type ReactNode } from "react";
import { Bookmark, Check, ChevronDown, Clock, Link as LinkIcon, Plus, Search, Trash2, X } from "lucide-react";
import { SearchSelect, type SearchSelectOption } from "../../components/ui/SearchSelect";
import { Button } from "../../components/ui/basics";
import { AskButton, type AskedQuestion } from "./AskExchanges";
import { Dropdown } from "./Dropdown";
import {
  BUILT_IN_VIEWS,
  RANGE_PRESETS,
  filterChips,
  hasTimeFilter,
  idList,
  parseBound,
  propertyFilters,
  withPropertyFilters,
  parseRelativeRange,
  searchSuggestions,
  timeLabel,
  toLocalInput,
  viewSearch,
  type ChipLookups,
  type FilterChip,
  type FilterKind,
  type ParamChanges,
  type SavedView,
} from "./exchangeFilters";

type Changes = ParamChanges;

export interface FilterOptions {
  subscriptions: SearchSelectOption[];
  partners: SearchSelectOption[];
  informationTypes: SearchSelectOption[];
  propertyKeys: SearchSelectOption[];
}

const inputClass =
  "h-9 w-full rounded-lg border border-ink-200 bg-white px-2.5 text-sm text-ink-900 focus:border-focus-400 focus:ring-2 focus:ring-focus-100 focus:outline-none";
const labelClass = "flex flex-col gap-1.5 text-xs font-medium text-ink-600";

export function QueryBar({
  searchParams,
  apply,
  options,
  lookups,
  onAsked,
}: {
  searchParams: URLSearchParams;
  /** Sets or drops parameters; changing a filter goes back to the first page. */
  apply: (changes: Changes) => void;
  options: FilterOptions;
  lookups: ChipLookups;
  /** A question the Ask box turned into the filter. */
  onAsked: (asked: AskedQuestion) => void;
}) {
  const chips = filterChips(searchParams, lookups);

  return (
    <div className="flex flex-wrap items-stretch gap-2">
      <div className="flex min-w-0 flex-[1_1_520px] flex-wrap items-center gap-1.5 rounded-xl border border-ink-200 bg-white py-1.5 pr-2 pl-3 focus-within:border-ink-300">
        <Search className="size-4 shrink-0 text-ink-500" aria-hidden />
        <TimeMenu searchParams={searchParams} apply={apply} />
        {chips.map((chip) => (
          <ChipEditor key={chip.id} chip={chip} searchParams={searchParams} apply={apply} options={options} />
        ))}
        <AddFilterMenu searchParams={searchParams} apply={apply} options={options} />
        <SearchField searchParams={searchParams} apply={apply} />
      </div>
      <ViewsMenu searchParams={searchParams} apply={apply} />
      <AskButton searchParams={searchParams} apply={apply} onAsked={onAsked} />
    </div>
  );
}

// --- time ---

function TimeMenu({ searchParams, apply }: { searchParams: URLSearchParams; apply: (c: Changes) => void }) {
  const range = searchParams.get("range");
  const active = hasTimeFilter(searchParams);
  return (
    <Dropdown
      width="w-[26rem]"
      trigger={({ open, toggle }) => (
        <button
          type="button"
          onClick={toggle}
          aria-expanded={open}
          aria-label={`Time: ${timeLabel(searchParams)}`}
          className={`inline-flex h-8 items-center gap-1.5 rounded-lg border px-2.5 text-[13px] whitespace-nowrap transition-colors ${
            active ? "border-crimson-200 bg-crimson-50 text-ink-800" : "border-ink-100 bg-ink-50 text-ink-700 hover:border-ink-300"
          }`}
        >
          <Clock className="size-3.5" aria-hidden />
          <span className="text-ink-500">Time</span>
          <strong className="font-semibold">{timeLabel(searchParams)}</strong>
          <ChevronDown className="size-3.5 text-ink-500" aria-hidden />
        </button>
      )}
    >
      {(close) => (
        <TimePanel
          range={range}
          from={searchParams.get("from")}
          to={searchParams.get("to")}
          onPick={(changes) => {
            apply(changes);
            close();
          }}
        />
      )}
    </Dropdown>
  );
}

function TimePanel({
  range,
  from,
  to,
  onPick,
}: {
  range: string | null;
  from: string | null;
  to: string | null;
  onPick: (changes: Changes) => void;
}) {
  const fromDate = parseBound(from, "from");
  const toDate = parseBound(to, "to");
  const [customFrom, setCustomFrom] = useState(fromDate ? toLocalInput(fromDate) : "");
  const [customTo, setCustomTo] = useState(toDate ? toLocalInput(toDate) : "");
  const [typed, setTyped] = useState("");
  const typedRange = parseRelativeRange(typed);
  const presetClass = (on: boolean) =>
    `flex h-8 w-full items-center rounded-lg px-2.5 text-left text-[13px] ${on ? "bg-ink-900 font-semibold text-white" : "text-ink-700 hover:bg-ink-50"}`;

  return (
    <div className="flex flex-col sm:flex-row">
      <div className="flex flex-col gap-0.5 border-b border-ink-100 p-1.5 sm:w-40 sm:border-r sm:border-b-0">
        <button type="button" className={presetClass(!range && !from && !to)} onClick={() => onPick({ range: null, from: null, to: null })}>
          Any time
        </button>
        {RANGE_PRESETS.map((p) => (
          <button key={p.value} type="button" className={presetClass(range === p.value)} onClick={() => onPick({ range: p.value, from: null, to: null })}>
            {p.label}
          </button>
        ))}
      </div>
      <form
        className="flex flex-1 flex-col gap-3 p-3.5"
        onSubmit={(e) => {
          e.preventDefault();
          if (typedRange) return onPick({ range: typedRange, from: null, to: null });
          onPick({ range: null, from: customFrom || null, to: customTo || null });
        }}
      >
        <label className={labelClass}>
          Last...
          <input
            className={inputClass}
            value={typed}
            onChange={(e) => setTyped(e.target.value)}
            placeholder="2h, 90m or 3d"
            aria-invalid={typed !== "" && !typedRange}
          />
        </label>
        <label className={labelClass}>
          From
          <input type="datetime-local" className={inputClass} value={customFrom} onChange={(e) => setCustomFrom(e.target.value)} />
        </label>
        <label className={labelClass}>
          To
          <input type="datetime-local" className={inputClass} value={customTo} onChange={(e) => setCustomTo(e.target.value)} />
        </label>
        <p className="text-xs text-ink-500">Times are in your timezone. Leave To empty for "until now".</p>
        <Button type="submit" size="sm" variant="primary" className="self-end" disabled={!typedRange && !customFrom && !customTo}>
          Apply
        </Button>
      </form>
    </div>
  );
}

// --- chips and their editors ---

function ChipEditor({
  chip,
  searchParams,
  apply,
  options,
}: {
  chip: FilterChip;
  searchParams: URLSearchParams;
  apply: (c: Changes) => void;
  options: FilterOptions;
}) {
  const remove = () => apply(chip.remove);
  const editable = !["retry", "scheduled", "aggregated"].includes(chip.kind);
  const value = chip.value && (
    <strong className={`max-w-56 truncate font-semibold text-ink-900 ${chip.mono ? "font-mono text-xs" : ""}`}>{chip.value}</strong>
  );

  // Laid out as Linear lays a filter out: what it is, how it matches (a menu of its own when it
  // can be turned round), and what was picked, which opens the editor.
  return (
    <span className="inline-flex h-8 items-center rounded-lg border border-crimson-200 bg-crimson-50 text-[13px]">
      <span className="pl-2.5 pr-1 text-ink-500">{chip.field}</span>
      {chip.turn ? (
        <OperatorMenu chip={chip} onTurn={(negated) => apply(chip.turn!.set(negated))} />
      ) : (
        chip.op && <span className="pr-1 text-ink-400">{chip.op}</span>
      )}
      {editable ? (
        <Dropdown
          width="w-80"
          trigger={({ open, toggle }) => (
            <button
              type="button"
              onClick={toggle}
              aria-expanded={open}
              aria-label={`Edit filter: ${chip.field} ${chip.op} ${chip.value}`.trim()}
              title={chip.title ?? "Change what this filter picks"}
              className="inline-flex h-8 items-center gap-1 px-1 hover:bg-crimson-100/60"
            >
              {value}
            </button>
          )}
        >
          {(close) => (
            <FilterEditor
              kind={chip.kind}
              index={chip.index}
              searchParams={searchParams}
              options={options}
              onApply={(changes) => {
                apply(changes);
                close();
              }}
            />
          )}
        </Dropdown>
      ) : (
        <span className="inline-flex items-center pr-1">{value}</span>
      )}
      <button
        type="button"
        onClick={remove}
        aria-label={`Remove filter ${chip.name}`}
        title="Remove this filter"
        className="mr-0.5 inline-flex size-6 items-center justify-center rounded-md text-crimson-700 hover:bg-crimson-100"
      >
        <X className="size-3.5" aria-hidden />
      </button>
    </span>
  );
}

/** How a filter matches - "is" or "is not", "contains" or "does not contain" - picked on its chip. */
function OperatorMenu({ chip, onTurn }: { chip: FilterChip; onTurn: (negated: boolean) => void }) {
  const turn = chip.turn!;
  return (
    <Dropdown
      width="w-64"
      trigger={({ open, toggle }) => (
        <button
          type="button"
          onClick={toggle}
          aria-expanded={open}
          aria-label={`How ${chip.name} matches: ${chip.op}`}
          title="Change how this filter matches"
          className="inline-flex h-8 items-center gap-0.5 px-1 text-ink-500 hover:bg-crimson-100/60 hover:text-ink-800"
        >
          {chip.op}
          <ChevronDown className="size-3" aria-hidden />
        </button>
      )}
    >
      {(close) => (
        <div role="menu" aria-label={`How ${chip.name} matches`} className="p-1.5">
          {turn.ops.map((op, i) => {
            const negated = i === 1;
            const current = turn.negated === negated;
            return (
              <button
                key={op}
                type="button"
                role="menuitemradio"
                aria-checked={current}
                onClick={() => {
                  if (!current) onTurn(negated);
                  close();
                }}
                className="flex w-full items-center gap-2 rounded-lg px-2 py-1.5 text-left text-sm text-ink-800 hover:bg-ink-50"
              >
                <span className="flex-1">
                  {op}
                  {negated && turn.hint && <span className="block text-xs text-ink-500">{turn.hint}</span>}
                </span>
                {current && <Check className="size-4 text-crimson-600" aria-hidden />}
              </button>
            );
          })}
        </div>
      )}
    </Dropdown>
  );
}

/**
 * Several of a kind picked at once - "is any of" - with what is picked listed first, so a long
 * list never hides it. Applying with nothing picked removes the filter.
 */
function MultiPicker({
  label,
  options,
  initial,
  onApply,
}: {
  label: string;
  options: SearchSelectOption[];
  initial: string[];
  onApply: (values: string[]) => void;
}) {
  const [chosen, setChosen] = useState<string[]>(initial);
  const [find, setFind] = useState("");
  const needle = find.trim().toLowerCase();
  const known = new Map(options.map((o) => [o.value, o]));
  const matches = (o: SearchSelectOption) =>
    !needle || [o.label, o.code, o.sublabel].some((text) => text?.toLowerCase().includes(needle));
  // One picked earlier but gone since still shows, by its id, so it can be unpicked.
  const picked = chosen.map((v) => known.get(v) ?? { value: v, label: `#${v}` }).filter(matches);
  const rest = options.filter((o) => !chosen.includes(o.value) && matches(o));
  const shown = rest.slice(0, MULTI_PICK_ROWS);
  const toggle = (v: string) => setChosen((c) => (c.includes(v) ? c.filter((x) => x !== v) : [...c, v]));

  return (
    <form
      className="flex flex-col gap-2"
      onSubmit={(e) => {
        e.preventDefault();
        onApply(chosen);
      }}
    >
      <input
        autoFocus
        aria-label={`Find ${label.toLowerCase()}`}
        placeholder="Search..."
        className={inputClass}
        value={find}
        onChange={(e) => setFind(e.target.value)}
      />
      <div role="group" aria-label={label} className="max-h-64 overflow-y-auto rounded-lg border border-ink-100 py-1">
        {[...picked, ...shown].map((o) => (
          <label key={o.value} title={o.title} className="flex cursor-pointer items-center gap-2 px-2.5 py-1.5 text-sm text-ink-800 hover:bg-ink-50">
            <input
              type="checkbox"
              checked={chosen.includes(o.value)}
              onChange={() => toggle(o.value)}
              className="size-4 shrink-0 cursor-pointer rounded accent-crimson-600"
            />
            <span className="min-w-0 flex-1 truncate">{o.label}</span>
            {o.hint && <span className="shrink-0 text-xs text-ink-500">{o.hint}</span>}
          </label>
        ))}
        {picked.length + shown.length === 0 && <p className="px-2.5 py-1.5 text-xs text-ink-500">Nothing matches.</p>}
        {rest.length > shown.length && (
          <p className="px-2.5 py-1.5 text-xs text-ink-500">{rest.length - shown.length} more - search to narrow the list.</p>
        )}
      </div>
      <div className="flex items-center justify-between gap-2">
        <span className="text-xs text-ink-500">{chosen.length === 0 ? "None picked" : `${chosen.length} picked`}</span>
        <Button type="submit" size="sm" variant="primary">
          Apply
        </Button>
      </div>
    </form>
  );
}

/** How many unpicked rows the picker lists before asking for a search. */
const MULTI_PICK_ROWS = 100;

/** Where a picker inside a filter panel goes, so each editor reads the same. */
function EditorShell({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-3 p-3.5">
      <p className="text-[11px] font-medium tracking-wide text-ink-500 uppercase">{title}</p>
      {children}
    </div>
  );
}

function TextFilterForm({
  label,
  initial,
  placeholder,
  hint,
  mono,
  multiline,
  onApply,
}: {
  label: string;
  initial: string;
  placeholder?: string;
  hint?: ReactNode;
  mono?: boolean;
  multiline?: boolean;
  onApply: (value: string) => void;
}) {
  const [value, setValue] = useState(initial);
  const id = useId();
  return (
    <form
      className="flex flex-col gap-3"
      onSubmit={(e) => {
        e.preventDefault();
        onApply(value.trim());
      }}
    >
      <label htmlFor={id} className={labelClass}>
        {label}
      </label>
      {multiline ? (
        <textarea
          id={id}
          autoFocus
          rows={4}
          className={`${inputClass} h-auto py-2 font-mono text-xs`}
          value={value}
          placeholder={placeholder}
          onChange={(e) => setValue(e.target.value)}
        />
      ) : (
        <input
          id={id}
          autoFocus
          className={`${inputClass} ${mono ? "font-mono text-[13px]" : ""}`}
          value={value}
          placeholder={placeholder}
          onChange={(e) => setValue(e.target.value)}
        />
      )}
      {hint && <p className="text-xs text-ink-500">{hint}</p>}
      <Button type="submit" size="sm" variant="primary" className="self-end">
        Apply
      </Button>
    </form>
  );
}

function FilterEditor({
  kind,
  index,
  searchParams,
  options,
  onApply,
}: {
  kind: FilterKind;
  /** Which promoted property a property chip edits; none adds one. */
  index?: number;
  searchParams: URLSearchParams;
  options: FilterOptions;
  onApply: (changes: Changes) => void;
}) {
  const pick = (label: string, key: string, opts: SearchSelectOption[]) => (
    <EditorShell title={label}>
      <MultiPicker
        label={label}
        options={opts}
        initial={idList(searchParams.get(key)).map(String)}
        onApply={(values) => onApply({ [key]: values.join(",") || null })}
      />
    </EditorShell>
  );

  switch (kind) {
    case "subscription":
      return pick("Subscriptions", "subscriptionId", options.subscriptions);
    case "partner":
      return pick("Partners", "partnerId", options.partners);
    case "informationType":
      return pick("Information types", "informationTypeId", options.informationTypes);
    case "property":
      return <PropertyEditor index={index} searchParams={searchParams} options={options} onApply={onApply} />;
    case "ids":
      return (
        <EditorShell title="Exchange ids">
          <TextFilterForm
            label="One or more ids"
            initial={searchParams.get("ids") ?? ""}
            placeholder="Paste ids, separated by commas or new lines"
            hint="Also finds their retries and the aggregation they belong to."
            multiline
            onApply={(v) => onApply({ ids: v.split(/[\s,|;]+/).filter(Boolean).join(",") || null })}
          />
        </EditorShell>
      );
    case "correlationId":
      return (
        <EditorShell title="Correlation id">
          <TextFilterForm
            label="Correlation id is"
            initial={searchParams.get("correlationId") ?? ""}
            mono
            hint="Every exchange that carried this correlation id: one flow, end to end."
            onApply={(v) => onApply({ correlationId: v || null })}
          />
        </EditorShell>
      );
    case "receiveAttempt":
      return (
        <EditorShell title="Created by a run">
          <TextFilterForm
            label="Run number"
            initial={searchParams.get("receiveAttemptId") ?? ""}
            hint="The exchanges one run of a scheduled receive created."
            onApply={(v) => onApply({ receiveAttemptId: /^\d+$/.test(v) ? v : null })}
          />
        </EditorShell>
      );
    case "error":
      return (
        <EditorShell title="Error message">
          <TextFilterForm
            label="Error message contains"
            initial={searchParams.get("error") ?? ""}
            mono
            placeholder="timeout, 503, sku is required"
            hint="Searches the exception text, ignoring case."
            onApply={(v) => onApply({ error: v || null })}
          />
        </EditorShell>
      );
    default:
      return null;
  }
}

function PropertyEditor({
  index,
  searchParams,
  options,
  onApply,
}: {
  index?: number;
  searchParams: URLSearchParams;
  options: FilterOptions;
  onApply: (changes: Changes) => void;
}) {
  const list = propertyFilters(searchParams);
  const editing = index === undefined ? undefined : list[index];
  const [key, setKey] = useState(editing?.key ?? "");
  const [value, setValue] = useState(editing?.text ?? "");
  const valueId = useId();
  const source = options.propertyKeys.find((o) => o.value === key);

  return (
    <EditorShell title="Promoted property">
      <form
        className="flex flex-col gap-3"
        onSubmit={(e) => {
          e.preventDefault();
          // Kept the way round it was; a new one is added beside the others.
          const edited = { key: key || null, text: value.trim() || null, negated: editing?.negated ?? false };
          onApply(withPropertyFilters(searchParams, editing ? list.map((p, i) => (i === index ? edited : p)) : [...list, edited]));
        }}
      >
        <div className={labelClass}>
          Property
          <SearchSelect
            aria-label="Filter by promoted property key"
            size="sm"
            value={key}
            clearLabel="Any property"
            options={options.propertyKeys}
            onChange={setKey}
          />
        </div>
        <label htmlFor={valueId} className={labelClass}>
          Contains
        </label>
        <input
          id={valueId}
          className={`${inputClass} font-mono text-[13px]`}
          value={value}
          onChange={(e) => setValue(e.target.value)}
          placeholder={key ? `${key} contains...` : "Any property value"}
          aria-label="Filter by promoted property"
        />
        {source?.title ? (
          <p className="rounded-lg border border-ink-100 bg-ink-50 px-2.5 py-2 font-mono text-[11px] whitespace-pre-line text-ink-600">
            {`Read from\n${source.title}`}
          </p>
        ) : (
          <p className="text-xs text-ink-500">
            {key ? "Leave it empty to find exchanges that carry this property at all." : "Matches inside any promoted property, ignoring case."}
          </p>
        )}
        <Button type="submit" size="sm" variant="primary" className="self-end" disabled={!key && !value.trim()}>
          Apply
        </Button>
      </form>
    </EditorShell>
  );
}

// --- adding a filter ---

const MENU: { group: string; items: { kind: FilterKind; label: string; hint?: string }[] }[] = [
  {
    group: "Where it went",
    items: [
      { kind: "subscription", label: "Subscription" },
      { kind: "partner", label: "Partner" },
      { kind: "informationType", label: "Information type" },
    ],
  },
  {
    group: "Identity",
    items: [
      { kind: "ids", label: "Exchange ids" },
      { kind: "correlationId", label: "Correlation id" },
      { kind: "receiveAttempt", label: "Created by a run" },
    ],
  },
  {
    group: "State",
    items: [
      { kind: "error", label: "Error message contains", hint: "Searches the exception text" },
      { kind: "scheduled", label: "Auto-retry scheduled" },
      { kind: "retry", label: "Is a retry" },
      { kind: "aggregated", label: "Part of an aggregation" },
    ],
  },
];

const TOGGLES: Partial<Record<FilterKind, string>> = { scheduled: "scheduled", retry: "retry", aggregated: "aggregated" };

function AddFilterMenu({
  searchParams,
  apply,
  options,
}: {
  searchParams: URLSearchParams;
  apply: (c: Changes) => void;
  options: FilterOptions;
}) {
  const [editing, setEditing] = useState<FilterKind | null>(null);
  const [find, setFind] = useState("");
  const [open, setOpen] = useState(false);
  const needle = find.trim().toLowerCase();
  const matches = (text: string) => !needle || text.toLowerCase().includes(needle);
  const propertyKeys = options.propertyKeys.filter((o) => matches(o.label) || matches(o.sublabel ?? ""));

  const close = () => {
    setOpen(false);
    setEditing(null);
    setFind("");
  };

  const itemClass = "flex w-full items-center gap-2 rounded-lg px-2 py-1.5 text-left text-sm text-ink-800 hover:bg-ink-50";

  return (
    <Dropdown
      width="w-80"
      open={open}
      onOpenChange={(o) => (o ? setOpen(true) : close())}
      trigger={({ toggle }) => (
        <button
          type="button"
          onClick={toggle}
          aria-expanded={open}
          className="inline-flex h-8 items-center gap-1 rounded-lg border border-dashed border-ink-300 px-2 text-[13px] font-medium text-ink-700 hover:bg-ink-50"
        >
          <Plus className="size-3.5" aria-hidden />
          Filter
        </button>
      )}
    >
      {() =>
        editing ? (
          <div>
            <button type="button" onClick={() => setEditing(null)} className="px-3.5 pt-3 text-xs font-medium text-ink-500 hover:text-ink-800">
              Back to filters
            </button>
            <FilterEditor
              kind={editing}
              searchParams={searchParams}
              options={options}
              onApply={(changes) => {
                apply(changes);
                close();
              }}
            />
          </div>
        ) : (
          <div>
            <label className="flex items-center gap-2 border-b border-ink-100 px-3 py-2">
              <Search className="size-4 text-ink-500" aria-hidden />
              <span className="sr-only">Find a filter</span>
              <input
                autoFocus
                value={find}
                onChange={(e) => setFind(e.target.value)}
                placeholder="Filter by..."
                className="h-7 flex-1 border-0 bg-transparent text-sm outline-none"
              />
            </label>
            <div role="menu" aria-label="Add a filter" className="max-h-[26rem] overflow-y-auto p-1.5">
              {MENU.map((g, i) => {
                const items = g.items.filter((it) => matches(it.label));
                const groupNode = items.length > 0 && (
                  <div key={g.group} className="pb-1">
                    <p className="px-2 pt-2 pb-1 text-[11px] font-medium tracking-wide text-ink-500 uppercase">{g.group}</p>
                    {items.map((it) => {
                      const toggleKey = TOGGLES[it.kind];
                      const on = toggleKey ? searchParams.get(toggleKey) === "1" : false;
                      return (
                        <button
                          key={it.kind}
                          type="button"
                          role="menuitem"
                          className={itemClass}
                          onClick={() => {
                            if (toggleKey) {
                              apply({ [toggleKey]: on ? null : "1" });
                              close();
                            } else setEditing(it.kind);
                          }}
                        >
                          <span className="flex-1">
                            {it.label}
                            {it.hint && <span className="block text-xs text-ink-500">{it.hint}</span>}
                          </span>
                          {on && <Check className="size-4 text-crimson-600" aria-hidden />}
                        </button>
                      );
                    })}
                  </div>
                );
                // Promoted properties read as their own group, between where it went and identity.
                if (i !== 1) return groupNode;
                return (
                  <div key="props-and-identity">
                    {propertyKeys.length > 0 || matches("promoted property") ? (
                      <div className="pb-1">
                        <p className="px-2 pt-2 pb-1 text-[11px] font-medium tracking-wide text-ink-500 uppercase">What it carried</p>
                        {propertyKeys.slice(0, 8).map((o) => (
                          <button
                            key={o.value}
                            type="button"
                            role="menuitem"
                            title={o.title}
                            className={itemClass}
                            onClick={() => {
                              const list = propertyFilters(searchParams);
                              apply(withPropertyFilters(searchParams, [...list, { key: o.value, text: null, negated: false }]));
                              close();
                            }}
                          >
                            <span className="flex-1 truncate font-mono text-[13px]">{o.label}</span>
                            {o.sublabel && <span className="max-w-32 truncate text-xs text-ink-500">{o.sublabel}</span>}
                          </button>
                        ))}
                        <button type="button" role="menuitem" className={itemClass} onClick={() => setEditing("property")}>
                          {propertyKeys.length > 8 ? `All ${propertyKeys.length} promoted properties...` : "Promoted property contains..."}
                        </button>
                      </div>
                    ) : null}
                    {groupNode}
                  </div>
                );
              })}
            </div>
          </div>
        )
      }
    </Dropdown>
  );
}

// --- search ---

function SearchField({ searchParams, apply }: { searchParams: URLSearchParams; apply: (c: Changes) => void }) {
  const [text, setText] = useState("");
  const [active, setActive] = useState(0);
  const [focused, setFocused] = useState(false);
  const suggestions = useMemo(() => searchSuggestions(text, searchParams), [text, searchParams]);
  const inputRef = useRef<HTMLInputElement>(null);
  const listId = useId();
  const open = focused && suggestions.length > 0;

  // "/" from anywhere on the page lands here, the way most search boxes on the web behave.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "/" || e.metaKey || e.ctrlKey || e.altKey) return;
      const t = e.target as HTMLElement | null;
      if (t && (t.closest("input, textarea, select, [contenteditable=true]") || t.closest("[role=dialog]"))) return;
      e.preventDefault();
      inputRef.current?.focus();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, []);

  const choose = (i: number) => {
    const s = suggestions[i];
    if (!s) return;
    apply(s.changes);
    setText("");
    inputRef.current?.blur();
  };

  return (
    <div className="relative flex min-w-40 flex-[1_1_200px] items-center">
      <input
        ref={inputRef}
        type="search"
        role="combobox"
        aria-label="Search exchanges"
        aria-expanded={open}
        aria-controls={listId}
        aria-activedescendant={open ? `${listId}-${active}` : undefined}
        value={text}
        onChange={(e) => {
          setText(e.target.value);
          setActive(0);
        }}
        onFocus={() => setFocused(true)}
        onBlur={() => setFocused(false)}
        onKeyDown={(e) => {
          if (e.key === "ArrowDown") {
            e.preventDefault();
            setActive((a) => Math.min(a + 1, suggestions.length - 1));
          } else if (e.key === "ArrowUp") {
            e.preventDefault();
            setActive((a) => Math.max(a - 1, 0));
          } else if (e.key === "Enter") {
            e.preventDefault();
            choose(active);
          } else if (e.key === "Escape" && text) {
            e.stopPropagation();
            setText("");
          }
        }}
        placeholder="Search id, correlation id or any property value"
        className="h-8 w-full border-0 bg-transparent text-sm text-ink-900 outline-none placeholder:text-ink-400"
      />
      <kbd className="ml-1 hidden rounded border border-ink-200 bg-ink-50 px-1.5 font-mono text-[11px] text-ink-500 sm:inline">/</kbd>
      {open && (
        <ul
          id={listId}
          role="listbox"
          aria-label="Search suggestions"
          className="absolute top-full right-0 left-0 z-40 mt-2 min-w-72 rounded-xl border border-ink-200 bg-white p-1.5 shadow-lg"
        >
          {suggestions.map((s, i) => (
            <li
              key={s.key}
              id={`${listId}-${i}`}
              role="option"
              aria-selected={i === active}
              // mousedown, not click: the input's blur closes the list before a click could land.
              onMouseDown={(e) => {
                e.preventDefault();
                choose(i);
              }}
              onMouseEnter={() => setActive(i)}
              className={`flex cursor-pointer flex-col rounded-lg px-2.5 py-2 ${i === active ? "bg-ink-50" : ""}`}
            >
              <span className="text-sm text-ink-800">
                {s.label} <code className="font-mono font-semibold text-ink-900">{s.value}</code>
              </span>
              <span className="text-xs text-ink-500">{s.hint}</span>
            </li>
          ))}
          <li role="presentation" className="border-t border-ink-100 px-2.5 pt-2 pb-1 text-xs text-ink-500">
            Up and down to choose, Enter to apply. Paste an exchange id to open it.
          </li>
        </ul>
      )}
    </div>
  );
}

// --- saved views ---

const VIEWS_KEY = "bitween.exchanges.views";

/** Saved in this browser only: a view is a link, so sharing one is "Copy link". */
function readViews(): SavedView[] {
  try {
    const raw = localStorage.getItem(VIEWS_KEY);
    const parsed = raw ? (JSON.parse(raw) as SavedView[]) : [];
    return Array.isArray(parsed) ? parsed.filter((v) => typeof v?.name === "string" && typeof v?.search === "string") : [];
  } catch {
    return [];
  }
}

function writeViews(views: SavedView[]) {
  try {
    localStorage.setItem(VIEWS_KEY, JSON.stringify(views));
  } catch {
    // Storage full or blocked: the view lasts as long as the page does.
  }
}

function ViewsMenu({ searchParams, apply }: { searchParams: URLSearchParams; apply: (c: Changes) => void }) {
  const [mine, setMine] = useState<SavedView[]>(readViews);
  const [naming, setNaming] = useState(false);
  const [name, setName] = useState("");
  const [copied, setCopied] = useState(false);
  const current = viewSearch(searchParams);
  const all = [...BUILT_IN_VIEWS, ...mine];
  const currentView = all.find((v) => v.search === current);

  const go = (view: SavedView) => {
    const target = new URLSearchParams(view.search);
    const changes: Changes = {};
    for (const k of new Set([...searchParams.keys(), ...target.keys()])) if (k !== "open") changes[k] = target.getAll(k);
    apply(changes);
  };

  return (
    <Dropdown
      align="right"
      width="w-80"
      onOpenChange={(o) => !o && (setNaming(false), setCopied(false))}
      trigger={({ open, toggle }) => (
        <button
          type="button"
          onClick={toggle}
          aria-expanded={open}
          aria-label={`Saved views: ${currentView?.name ?? "Custom"}`}
          className="inline-flex h-11 items-center gap-2 rounded-xl border border-ink-200 bg-white px-3 text-[13px] text-ink-800 hover:bg-ink-50"
        >
          <Bookmark className="size-4 text-ink-500" aria-hidden />
          <span className="text-ink-500">View</span>
          <strong className="max-w-40 truncate font-semibold">{currentView?.name ?? "Custom"}</strong>
          <ChevronDown className="size-3.5 text-ink-500" aria-hidden />
        </button>
      )}
    >
      {(close) => (
        <div className="p-1.5">
          <div role="menu" aria-label="Saved views">
            {all.map((v) => (
              <div key={`${v.builtIn ? "b" : "m"}-${v.name}`} className="group flex items-center rounded-lg hover:bg-ink-50">
                <button
                  type="button"
                  role="menuitem"
                  onClick={() => {
                    go(v);
                    close();
                  }}
                  className="flex min-w-0 flex-1 flex-col px-2.5 py-2 text-left"
                >
                  <span className={`text-sm text-ink-900 ${v === currentView ? "font-semibold" : "font-medium"}`}>{v.name}</span>
                  <span className="truncate text-xs text-ink-500">{v.description ?? (v.search ? "Saved in this browser" : "No filters")}</span>
                </button>
                {!v.builtIn && (
                  <button
                    type="button"
                    aria-label={`Delete view ${v.name}`}
                    onClick={() => {
                      const next = mine.filter((m) => m !== v);
                      setMine(next);
                      writeViews(next);
                    }}
                    className="mr-1 rounded-md p-1.5 text-ink-400 opacity-0 group-hover:opacity-100 hover:bg-ink-100 hover:text-danger-700 focus:opacity-100"
                  >
                    <Trash2 className="size-3.5" aria-hidden />
                  </button>
                )}
              </div>
            ))}
          </div>
          <div className="mt-1 border-t border-ink-100 pt-1">
            {naming ? (
              <form
                className="flex gap-1.5 p-1.5"
                onSubmit={(e) => {
                  e.preventDefault();
                  const n = name.trim();
                  if (!n) return;
                  const next = [...mine.filter((m) => m.name !== n), { name: n, search: current }];
                  setMine(next);
                  writeViews(next);
                  setNaming(false);
                  setName("");
                }}
              >
                <input
                  autoFocus
                  aria-label="View name"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="Name this view"
                  className={`${inputClass} h-8`}
                />
                <Button type="submit" size="sm" variant="primary" disabled={!name.trim()}>
                  Save
                </Button>
              </form>
            ) : (
              <button
                type="button"
                disabled={!current || !!currentView}
                onClick={() => setNaming(true)}
                className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left text-sm text-ink-800 hover:bg-ink-50 disabled:text-ink-400 disabled:hover:bg-transparent"
              >
                <Plus className="size-3.5" aria-hidden />
                Save current filters as a view
              </button>
            )}
            <button
              type="button"
              onClick={async () => {
                const url = new URL(window.location.href);
                url.search = current;
                try {
                  await navigator.clipboard.writeText(url.toString());
                  setCopied(true);
                } catch {
                  setCopied(false);
                }
              }}
              className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left text-sm text-ink-800 hover:bg-ink-50"
            >
              {copied ? <Check className="size-3.5 text-ok-600" aria-hidden /> : <LinkIcon className="size-3.5" aria-hidden />}
              {copied ? "Link copied" : "Copy link to this view"}
            </button>
          </div>
        </div>
      )}
    </Dropdown>
  );
}
