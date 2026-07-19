import { useEffect, useRef, useState } from "react";
import { Link } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { ArrowUpRight, Braces, ChevronDown, ChevronRight, Search } from "lucide-react";
import { api, type AdapterInfo, type AdapterKind, type PartnerRow } from "../../api";
import { Button } from "../ui/basics";
import { Field } from "../ui/forms";
import { SearchSelect } from "../ui/SearchSelect";
import { SummaryDisclosure } from "../ui/SummaryDisclosure";

export function useAdapterCatalog(kind: AdapterKind) {
  return useQuery({
    queryKey: ["adapters", kind],
    queryFn: () => api.listAdapters(kind),
    staleTime: Infinity,
  });
}

interface ReferenceToken {
  label: string;
  token: string;
  /** Globals resolve to one literal value; partner properties resolve per-exchange. */
  value?: string;
}

/** All insertable reference tokens: every global key + every known partner property. */
function useReferenceTokens() {
  const sets = useQuery({ queryKey: ["value-sets"], queryFn: () => api.listValueSets(), staleTime: 60_000 });
  const partners = useQuery({ queryKey: ["partners"], queryFn: () => api.listPartners(), staleTime: 60_000 });
  const globals: ReferenceToken[] = (sets.data ?? []).flatMap((s) =>
    Object.entries(s.values).map(([key, value]) => ({
      label: `${s.id}.${key}`,
      token: `{{globals.${s.id}.${key}}}`,
      value,
    })),
  );
  const partnerKeys: ReferenceToken[] = [
    ...new Set((partners.data ?? []).flatMap((p) => Object.keys(p.adapterProperties))),
  ].map((key) => ({ label: `partner.${key}`, token: `{{partner.${key}}}` }));
  return { globals, partnerKeys, partners: partners.data ?? [] };
}

/**
 * Searchable popover for inserting a `{{globals.…}}` / `{{partner.…}}`
 * reference. Globals show their literal value; partner properties resolve
 * per-exchange, so they show a note instead.
 */
function ReferenceMenu({
  globals,
  partnerKeys,
  onPick,
  label,
}: {
  globals: ReferenceToken[];
  partnerKeys: ReferenceToken[];
  onPick: (token: string) => void;
  label: string;
}) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (!ref.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && setOpen(false);
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  const needle = query.trim().toLowerCase();
  const matches = (t: ReferenceToken) =>
    !needle || t.label.toLowerCase().includes(needle) || (t.value?.toLowerCase().includes(needle) ?? false);
  const filteredGlobals = globals.filter(matches);
  const filteredPartnerKeys = partnerKeys.filter(matches);
  const noMatches = filteredGlobals.length === 0 && filteredPartnerKeys.length === 0;

  const pick = (token: string) => {
    onPick(token);
    setOpen(false);
    setQuery("");
  };

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        title="Insert a reference"
        aria-label={label}
        className="mt-1 rounded-md p-1.5 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
      >
        <Braces className="size-4" />
      </button>
      {open && (
        <div className="absolute top-full right-0 z-40 mt-1.5 w-72 rounded-xl border border-ink-100 bg-white p-2 shadow-lg">
          <div className="relative mb-1.5">
            <Search className="pointer-events-none absolute top-1/2 left-2.5 size-3.5 -translate-y-1/2 text-ink-400" />
            <input
              autoFocus
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Search references"
              aria-label="Search references"
              className="h-8 w-full rounded-md border border-ink-200 bg-white pr-2 pl-8 text-[13px] placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
            />
          </div>
          <div className="max-h-64 space-y-1 overflow-y-auto">
            {noMatches && <p className="px-2 py-2 text-[13px] text-ink-400">No matches.</p>}
            {filteredGlobals.length > 0 && (
              <div>
                <p className="px-2 py-1 text-[11px] font-medium tracking-wide text-ink-400 uppercase">
                  Global values
                </p>
                {filteredGlobals.map((t) => (
                  <button
                    key={t.token}
                    type="button"
                    onClick={() => pick(t.token)}
                    className="flex w-full items-center justify-between gap-2 rounded-lg px-2 py-1.5 text-left hover:bg-ink-50"
                  >
                    <span className="min-w-0 truncate font-mono text-xs text-ink-800">{t.label}</span>
                    <span className="max-w-24 shrink-0 truncate text-xs text-ink-400" title={t.value}>
                      {t.value}
                    </span>
                  </button>
                ))}
              </div>
            )}
            {filteredPartnerKeys.length > 0 && (
              <div>
                <p className="px-2 py-1 text-[11px] font-medium tracking-wide text-ink-400 uppercase">
                  Partner properties
                </p>
                {filteredPartnerKeys.map((t) => (
                  <button
                    key={t.token}
                    type="button"
                    onClick={() => pick(t.token)}
                    className="flex w-full items-center justify-between gap-2 rounded-lg px-2 py-1.5 text-left hover:bg-ink-50"
                  >
                    <span className="min-w-0 truncate font-mono text-xs text-ink-800">{t.label}</span>
                    <span className="shrink-0 text-xs text-ink-400">resolved per partner</span>
                  </button>
                ))}
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

/**
 * What the references inside a field's value resolve to, shown right under
 * it: globals show their literal value (linking to the set); partner
 * properties show how many partners define them and drill down to every
 * partner's value.
 */
function ReferenceHints({
  value,
  globals,
  partners,
}: {
  value: string;
  globals: ReferenceToken[];
  partners: PartnerRow[];
}) {
  const [openKeys, setOpenKeys] = useState<Set<string>>(new Set());

  const globalRefs = [...new Set([...value.matchAll(/\{\{globals\.([\w-]+)\.([\w-]+)\}\}/g)].map((m) => m[0]))];
  const partnerRefs = [...new Set([...value.matchAll(/\{\{partner\.([\w-]+)\}\}/g)].map((m) => m[1]))];
  if (globalRefs.length === 0 && partnerRefs.length === 0) return null;

  const realPartners = partners.filter((p) => !p.isSystem);
  const toggle = (key: string) =>
    setOpenKeys((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });

  return (
    <div className="mt-1.5 space-y-1">
      {globalRefs.map((token) => {
        const ref = globals.find((g) => g.token === token);
        const setId = token.match(/\{\{globals\.([\w-]+)\./)?.[1];
        return (
          <div key={token} className="flex flex-wrap items-center gap-1.5 text-xs">
            <Link
              to={`/global-values/${setId}`}
              className="font-mono text-ink-500 hover:text-crimson-700 hover:underline"
            >
              {token}
            </Link>
            <span className="text-ink-300">→</span>
            {ref ? (
              <code className="rounded bg-ink-100 px-1.5 py-0.5 font-mono text-[11px] break-all text-ink-700">
                {ref.value}
              </code>
            ) : (
              <span className="font-medium text-crimson-700">not found — check the set and key</span>
            )}
          </div>
        );
      })}

      {partnerRefs.map((key) => {
        const withValue = realPartners.filter((p) => key in p.adapterProperties);
        const without = realPartners.length - withValue.length;
        const open = openKeys.has(key);
        return (
          <div key={key} className="text-xs">
            <button
              type="button"
              onClick={() => toggle(key)}
              aria-expanded={open}
              className="flex items-center gap-1 text-ink-500 hover:text-ink-700"
            >
              {open ? <ChevronDown className="size-3" aria-hidden /> : <ChevronRight className="size-3" aria-hidden />}
              <span className="font-mono">{`{{partner.${key}}}`}</span>
              <span className="text-ink-300">→</span>
              <span className={withValue.length === 0 ? "font-medium text-crimson-700" : "font-medium text-ink-700"}>
                {withValue.length === 0
                  ? "no partner defines it"
                  : `defined by ${withValue.length} of ${realPartners.length} partner${realPartners.length === 1 ? "" : "s"}`}
              </span>
            </button>
            {open && (
              <ul className="mt-1 ml-4 space-y-0.5 border-l border-ink-100 pl-2.5">
                {withValue.map((p) => (
                  <li key={p.id} className="flex flex-wrap items-center gap-1.5">
                    <Link
                      to={`/partners/${p.id}`}
                      className="font-medium text-ink-700 hover:text-crimson-700 hover:underline"
                    >
                      {p.name}
                    </Link>
                    <span className="text-ink-300">→</span>
                    <code className="rounded bg-ink-100 px-1.5 py-0.5 font-mono text-[11px] break-all text-ink-700">
                      {p.adapterProperties[key]}
                    </code>
                  </li>
                ))}
                {without > 0 && (
                  <li className="text-ink-400">
                    {without} partner{without === 1 ? " doesn't" : "s don't"} set it — their exchanges resolve it
                    empty.
                  </li>
                )}
              </ul>
            )}
          </div>
        );
      })}
    </div>
  );
}

/** One adapter property: grows with content, can insert reference tokens. */
function PropField({
  prop,
  value,
  disabled,
  onChange,
}: {
  prop: AdapterInfo["props"][number];
  value: string;
  disabled: boolean;
  onChange: (value: string) => void;
}) {
  const { globals, partnerKeys, partners } = useReferenceTokens();
  const [replacing, setReplacing] = useState(false);
  const masked = prop.secret && !!value && !replacing;

  return (
    <Field
      label={prop.optional ? prop.key : `${prop.key} *`}
      htmlFor={`prop-${prop.key}`}
      hint={prop.description}
    >
      {masked ? (
        <div className="flex h-9.5 items-center justify-between rounded-lg border border-ink-200 bg-ink-50 px-3">
          <span className="font-mono text-sm tracking-widest text-ink-400">••••••••</span>
          {!disabled && (
            <Button size="sm" variant="ghost" onClick={() => { onChange(""); setReplacing(true); }}>
              Replace
            </Button>
          )}
        </div>
      ) : (
        <div className="flex items-start gap-1">
          <div className="grid min-w-0 flex-1 grid-cols-[minmax(0,1fr)]">
            <textarea
              id={`prop-${prop.key}`}
              rows={1}
              value={value}
              disabled={disabled}
              placeholder={prop.default}
              onChange={(e) => onChange(e.target.value)}
              className="[grid-area:1/1] w-full resize-none overflow-hidden rounded-lg border border-ink-200 bg-white px-3 py-2 text-sm leading-5 break-words whitespace-pre-wrap text-ink-900 placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none disabled:bg-ink-50 disabled:text-ink-500"
            />
            <span aria-hidden className="[grid-area:1/1] invisible border px-3 py-2 text-sm leading-5 break-words whitespace-pre-wrap">
              {value || prop.default || " "}{" "}
            </span>
          </div>
          {!disabled && !prop.secret && (globals.length > 0 || partnerKeys.length > 0) && (
            <ReferenceMenu
              globals={globals}
              partnerKeys={partnerKeys}
              onPick={(token) => onChange(value + token)}
              label={`Insert a reference into ${prop.key}`}
            />
          )}
        </div>
      )}
      {!masked && <ReferenceHints value={value} globals={globals} partners={partners} />}
    </Field>
  );
}

/**
 * One pipeline slot (receiver/validator/mapper/handler): pick an adapter,
 * then configure it through fields generated from its startup metadata.
 */
export function AdapterConfig({
  kind,
  adapterId,
  properties,
  onChange,
  disabled,
  required = false,
  noneLabel = "None",
  mapperEditorHref,
}: {
  kind: AdapterKind;
  adapterId: string | null;
  properties: Record<string, string>;
  onChange: (adapterId: string | null, properties: Record<string, string>) => void;
  disabled: boolean;
  required?: boolean;
  /** What "no adapter" means here, e.g. "None — payload passes through unchanged". */
  noneLabel?: string;
  /** When the native JSON mapper is selected, where its visual editor lives. */
  mapperEditorHref?: string;
}) {
  const catalog = useAdapterCatalog(kind);
  const adapter = catalog.data?.find((a) => a.id === adapterId);

  // Required fields stay visible; optional ones tuck behind an expander so
  // the busiest forms (HTTP handler & friends) aren't a wall of inputs.
  const requiredProps = adapter?.props.filter((p) => !p.optional) ?? [];
  const optionalProps = adapter?.props.filter((p) => p.optional) ?? [];
  const setOptionalCount = optionalProps.filter((p) => (properties[p.key] ?? "").trim() !== "").length;

  const renderProp = (prop: AdapterInfo["props"][number]) => (
    <PropField
      key={prop.key}
      prop={prop}
      value={properties[prop.key] ?? ""}
      disabled={disabled}
      onChange={(v) => onChange(adapterId, { ...properties, [prop.key]: v })}
    />
  );

  const pick = (id: string) => {
    if (id === "") return onChange(null, {});
    const next = catalog.data?.find((a) => a.id === id);
    // prefill defaults so required fields with defaults start valid
    const seeded = Object.fromEntries(
      (next?.props ?? []).filter((p) => !p.optional && p.default).map((p) => [p.key, p.default!]),
    );
    onChange(id, seeded);
  };

  return (
    <div className="space-y-4">
      <div className="max-w-sm">
        <SearchSelect
          aria-label={`${kind} adapter`}
          value={adapterId ?? ""}
          disabled={disabled || catalog.isPending}
          onChange={pick}
          placeholder={`Pick a ${kind}…`}
          clearLabel={required ? undefined : noneLabel}
          options={(catalog.data ?? []).map((a) => ({
            value: a.id,
            label: a.label,
            code: a.id,
            hint: a.native ? "Native" : `v${a.versions.at(-1)}`,
          }))}
        />
      </div>
      {adapter && adapter.props.length > 0 && (
        <div className="space-y-4">
          {requiredProps.length > 0 && (
            <div className="grid gap-4 sm:grid-cols-2">{requiredProps.map(renderProp)}</div>
          )}
          {optionalProps.length > 0 && (
            <SummaryDisclosure
              key={adapterId}
              defaultOpen={setOptionalCount > 0}
              changeLabel="Show"
              summary={
                <>
                  {optionalProps.length} optional field{optionalProps.length === 1 ? "" : "s"}
                  {setOptionalCount > 0 ? ` · ${setOptionalCount} set` : ""}
                </>
              }
            >
              <div className="grid gap-4 sm:grid-cols-2">{optionalProps.map(renderProp)}</div>
            </SummaryDisclosure>
          )}
        </div>
      )}
      {adapter && adapter.id === "NativeJSONMapper" && (
        mapperEditorHref ? (
          <Link
            to={mapperEditorHref}
            className="inline-flex items-center gap-1.5 rounded-lg border border-ink-200 bg-white px-3 py-2 text-[13px] font-medium text-crimson-700 hover:border-ink-300 hover:bg-ink-50"
          >
            Open the visual mapping editor
            <ArrowUpRight className="size-3.5" aria-hidden />
          </Link>
        ) : (
          <p className="rounded-lg bg-ink-50 px-3 py-2 text-[13px] text-ink-500">
            This mapper is configured through the visual mapping editor.
          </p>
        )
      )}
    </div>
  );
}
