import type { ExchangePropertyFilter, ExchangeQuery, ExchangeStatus, NegatableExchangeFilter } from "../../api";

/** Parameters to set, drop (null) or, for one that repeats, set to several values. */
export type ParamChanges = Record<string, string | string[] | null | undefined>;

/**
 * Everything the exchanges list can be narrowed by lives in the URL, so a view is a link: it
 * survives a reload, and pasting it to a colleague shows them the same list. This module is the
 * one place that reads and writes those parameters.
 *
 * Every parameter an older link might carry still means what it meant - the dashboard, the retry
 * policy usage panel and the scheduled runs all link in with `status`, `ids`, `subscriptionId`,
 * `receiveAttemptId` and the rest, and `from`/`to` as bare dates.
 */

export const PAGE_SIZE = 25;

/**
 * The backend stops counting matches past this and returns `COUNT_CAP + 1` instead, because an
 * exact total has to visit every matching row and was costing more than fetching the rows did.
 * So a total above the cap means "at least this many", shown as "10,000+".
 * Kept in step with `CountCap` in Xchanges/Search.cs.
 */
export const COUNT_CAP = 10_000;

export const STATUSES: ExchangeStatus[] = ["failed", "badResponse", "processing", "success"];

/** Everything except paging and the open row counts as "a filter" for the Clear affordance. */
export const FILTER_KEYS = [
  "status",
  "subscriptionId",
  "partnerId",
  "informationTypeId",
  "ids",
  "receiveAttemptId",
  "correlationId",
  "propertyKey",
  "property",
  "prop",
  "error",
  "retry",
  "scheduled",
  "aggregated",
  "range",
  "from",
  "to",
  "latest",
  "not",
] as const;

export type FilterKey = (typeof FILTER_KEYS)[number];

// --- turned round ---

/**
 * The parameters each filter that can be turned round is set by. `not` lists the filters turned
 * round - `not=subscription,error` - while each keeps its own parameter, so every link made before
 * there was a `not` still means what it did.
 */
const NEGATABLE: Record<NegatableExchangeFilter, string[]> = {
  subscription: ["subscriptionId"],
  partner: ["partnerId"],
  informationType: ["informationTypeId"],
  property: ["propertyKey", "property"],
  error: ["error"],
  retry: ["retry"],
  scheduled: ["scheduled"],
  aggregated: ["aggregated"],
};

// Own keys only: the URL is anyone's, and `in` would also take "constructor".
const isNegatable = (kind: string): kind is NegatableExchangeFilter => Object.hasOwn(NEGATABLE, kind);

/** The filters turned round, counting only those that are set. */
export function negatedFilters(sp: URLSearchParams): NegatableExchangeFilter[] {
  return (sp.get("not") ?? "")
    .split(",")
    .filter(isNegatable)
    .filter((kind) => NEGATABLE[kind].some((p) => sp.has(p)));
}

/** The `not` parameter with one filter turned round, or back. */
export function notWith(sp: URLSearchParams, kind: NegatableExchangeFilter, negated: boolean): string | null {
  const kinds = new Set(negatedFilters(sp));
  if (negated) kinds.add(kind);
  else kinds.delete(kind);
  return [...kinds].sort().join(",") || null;
}

// --- promoted properties ---

/**
 * Promoted properties, any number at once, all of which have to hold. Each named one is a `prop`
 * parameter of its own - `prop=orderNo:1001`, `prop=city`, `prop=!customer:zed` - its key, then
 * the text to find in that key's value, and a leading "!" to turn it round. Text to find in any
 * property has no key to pair with, so it keeps `property`, turned round by `not=property`.
 *
 * `propertyKey` is how a link named its one property before there could be several. It is still
 * read, with `property` as its text, and written over the first time the properties change.
 */
const parseProp = (raw: string): ExchangePropertyFilter => {
  const negated = raw.startsWith("!");
  const body = negated ? raw.slice(1) : raw;
  // The first colon ends the key, as it does where the values are stored; the text may hold more.
  const colon = body.indexOf(":");
  return colon < 0
    ? { key: body, text: null, negated }
    : { key: body.slice(0, colon), text: body.slice(colon + 1) || null, negated };
};

const formatProp = (p: ExchangePropertyFilter) => `${p.negated ? "!" : ""}${p.key}${p.text ? `:${p.text}` : ""}`;

/** The promoted-property filters, named ones first and text in any property last. */
export function propertyFilters(sp: URLSearchParams): ExchangePropertyFilter[] {
  const named = sp.getAll("prop").map(parseProp).filter((p) => p.key);
  const legacyKey = sp.get("propertyKey");
  const text = sp.get("property");
  const negated = (sp.get("not") ?? "").split(",").includes("property");
  if (legacyKey) return [{ key: legacyKey, text: text || null, negated }, ...named];
  return text ? [...named, { key: null, text, negated }] : named;
}

/** The parameters that say `list`, replacing whatever said the properties before. */
export function withPropertyFilters(sp: URLSearchParams, list: ExchangePropertyFilter[]): ParamChanges {
  const named = list.filter((p) => p.key);
  // Text in any property is one filter: a second replaces the first.
  const any = list.filter((p) => !p.key).at(-1);
  const not = new Set<NegatableExchangeFilter>(negatedFilters(sp).filter((kind) => kind !== "property"));
  if (any?.negated) not.add("property");
  return {
    prop: named.length > 0 ? named.map(formatProp) : null,
    propertyKey: null,
    property: any?.text ?? null,
    not: [...not].sort().join(",") || null,
  };
}

/** Shown as "10,000+" past the cap. */
export const countLabel = (n: number) => (n > COUNT_CAP ? `${COUNT_CAP.toLocaleString()}+` : n.toLocaleString());

// --- time ---

/** The quick picks in the time menu, in the order they are offered. */
export const RANGE_PRESETS: { value: string; label: string }[] = [
  { value: "15m", label: "Last 15 minutes" },
  { value: "1h", label: "Last hour" },
  { value: "4h", label: "Last 4 hours" },
  { value: "24h", label: "Last 24 hours" },
  { value: "7d", label: "Last 7 days" },
  { value: "30d", label: "Last 30 days" },
  { value: "today", label: "Today" },
  { value: "yesterday", label: "Yesterday" },
];

const RELATIVE = /^(\d{1,4})\s*(m|h|d)$/i;

/** Whether text typed into the time menu, such as "2h" or "90m", reads as a relative range. */
export const parseRelativeRange = (text: string): string | null => {
  const m = text.trim().toLowerCase().match(RELATIVE);
  if (!m || Number(m[1]) === 0) return null;
  return `${Number(m[1])}${m[2]}`;
};

const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate());

/**
 * A `range` value as a window ending at (or, for "yesterday", before) `now`. Resolved when the
 * list is asked for rather than when the link was made, so "last hour" on a live list keeps
 * meaning the last hour.
 */
export function resolveRange(range: string, now: Date): { from: Date; to?: Date } | null {
  if (range === "today") return { from: startOfDay(now) };
  if (range === "yesterday") {
    const today = startOfDay(now);
    return { from: new Date(today.getTime() - 86_400_000), to: new Date(today.getTime() - 1) };
  }
  const m = range.match(RELATIVE);
  if (!m) return null;
  const n = Number(m[1]);
  const unit = { m: 60_000, h: 3_600_000, d: 86_400_000 }[m[2].toLowerCase() as "m" | "h" | "d"];
  return { from: new Date(now.getTime() - n * unit) };
}

/** A `range` value as words: "Last 2 hours". */
export function rangeLabel(range: string): string {
  const preset = RANGE_PRESETS.find((p) => p.value === range);
  if (preset) return preset.label;
  const m = range.match(RELATIVE);
  if (!m) return range;
  const n = Number(m[1]);
  const unit = { m: "minute", h: "hour", d: "day" }[m[2].toLowerCase() as "m" | "h" | "d"];
  return `Last ${n} ${unit}${n === 1 ? "" : "s"}`;
}

/**
 * `from`/`to` as the URL carries them. A bare date is how links were made before times could be
 * picked, and still means the whole day: from its first moment, to its last.
 */
export function parseBound(value: string | null, edge: "from" | "to"): Date | null {
  if (!value) return null;
  if (/^\d{4}-\d{2}-\d{2}$/.test(value)) return new Date(`${value}T${edge === "from" ? "00:00:00" : "23:59:59"}`);
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? null : d;
}

const pad = (n: number) => String(n).padStart(2, "0");

/** A local date-time the way `<input type="datetime-local">` and the URL hold it: 2026-10-11T14:30. */
export const toLocalInput = (d: Date) =>
  `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;

/** Short local wording for a custom range bound: "11 Oct 14:30", or "11 Oct" for a whole day. */
function boundLabel(value: string, edge: "from" | "to"): string {
  const d = parseBound(value, edge);
  if (!d) return value;
  const day = d.toLocaleDateString(undefined, { day: "numeric", month: "short" });
  return /^\d{4}-\d{2}-\d{2}$/.test(value) ? day : `${day} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** What the time chip says. */
export function timeLabel(sp: URLSearchParams): string {
  const range = sp.get("range");
  if (range) return rangeLabel(range);
  const from = sp.get("from");
  const to = sp.get("to");
  if (from && to) return `${boundLabel(from, "from")} to ${boundLabel(to, "to")}`;
  if (from) return `Since ${boundLabel(from, "from")}`;
  if (to) return `Until ${boundLabel(to, "to")}`;
  return "Any time";
}

export const hasTimeFilter = (sp: URLSearchParams) => sp.has("range") || sp.has("from") || sp.has("to");

// --- reading the URL ---

const num = (v: string | null) => (v && !Number.isNaN(Number(v)) ? Number(v) : undefined);
const flag = (v: string | null) => v === "1" || undefined;

/** A parameter holding one id or several, comma-separated: "16" or "16,17". */
export const idList = (v: string | null): number[] =>
  (v ?? "")
    .split(",")
    .map(Number)
    .filter((n) => Number.isInteger(n) && n > 0);
const ids = (v: string | null) => {
  const list = idList(v);
  return list.length > 0 ? list : undefined;
};

/** The list's query, as of `now` - which only matters for a relative `range`. */
export function readQuery(sp: URLSearchParams, now: Date = new Date()): ExchangeQuery {
  const range = sp.get("range");
  const resolved = range ? resolveRange(range, now) : null;
  const from = resolved ? resolved.from : parseBound(sp.get("from"), "from");
  const to = resolved ? resolved.to : parseBound(sp.get("to"), "to");
  const status = sp.get("status");
  const not = negatedFilters(sp);
  const properties = propertyFilters(sp);

  return {
    status: STATUSES.includes(status as ExchangeStatus) ? (status as ExchangeStatus) : undefined,
    subscriptionIds: ids(sp.get("subscriptionId")),
    partnerIds: ids(sp.get("partnerId")),
    informationTypeIds: ids(sp.get("informationTypeId")),
    not: not.length > 0 ? not : undefined,
    ids: sp.get("ids") ?? undefined,
    receiveAttemptId: num(sp.get("receiveAttemptId")),
    correlationId: sp.get("correlationId") ?? undefined,
    latest: flag(sp.get("latest")),
    properties: properties.length > 0 ? properties : undefined,
    errorContains: sp.get("error") ?? undefined,
    isRetry: flag(sp.get("retry")),
    scheduledRetry: flag(sp.get("scheduled")),
    aggregated: flag(sp.get("aggregated")),
    from: from?.toISOString(),
    to: to?.toISOString(),
    offset: num(sp.get("offset")) ?? 0,
    limit: PAGE_SIZE,
  };
}

/**
 * The URL with some parameters set or dropped. Changing any filter goes back to the first page,
 * since the page the list was on belongs to the old set of rows.
 */
export function withParams(
  sp: URLSearchParams,
  changes: ParamChanges,
  { resetOffset = true }: { resetOffset?: boolean } = {},
): URLSearchParams {
  const next = new URLSearchParams(sp);
  for (const [key, value] of Object.entries(changes)) {
    next.delete(key);
    // A list is a parameter that repeats, one value each: `prop=orderNo:1&prop=city`.
    for (const v of Array.isArray(value) ? value : [value]) if (v) next.append(key, v);
  }
  if (resetOffset) next.delete("offset");

  // A filter that is gone takes its "not" with it, so the same filter added again later starts
  // the right way round rather than silently turned.
  if (next.has("not")) {
    const not = [...new Set(negatedFilters(next))].sort().join(",");
    if (not) next.set("not", not);
    else next.delete("not");
  }
  return next;
}

/** The filters alone, in a fixed order, so two URLs that select the same rows compare equal. */
export const filterSignature = (sp: URLSearchParams) => FILTER_KEYS.map((k) => sp.getAll(k).join("\u0001")).join("\u0000");

export const activeFilterCount = (sp: URLSearchParams) => FILTER_KEYS.filter((k) => sp.has(k)).length;

// --- chips ---

/** The editors the add-filter menu and the chips open. */
export type FilterKind =
  | "subscription"
  | "partner"
  | "informationType"
  | "property"
  | "ids"
  | "correlationId"
  | "receiveAttempt"
  | "error"
  | "retry"
  | "scheduled"
  | "aggregated";

export interface FilterChip {
  /** Tells two chips of one kind apart: there can be several promoted properties. */
  id: string;
  kind: FilterKind;
  /** Which promoted property it is, for its editor. */
  index?: number;
  /** What it is called whichever way round it reads, for its remove button: "Subscription". */
  name: string;
  field: string;
  op: string;
  value: string;
  mono?: boolean;
  /** The names behind a count such as "3 subscriptions", shown on hover. */
  title?: string;
  /** What removing it changes in the URL. */
  remove: ParamChanges;
  /** For a filter that can be turned round: its two readings, which one it is now, and how to change it. */
  turn?: {
    negated: boolean;
    ops: [string, string];
    /** What the turned-round reading also keeps, which is easy to forget. */
    hint?: string;
    set: (negated: boolean) => ParamChanges;
  };
}

export interface ChipLookups {
  subscriptionName: (id: number) => string | undefined;
  partnerName: (id: number) => string | undefined;
  informationTypeName: (id: number) => string | undefined;
}

const drop = (...keys: string[]): ParamChanges => Object.fromEntries(keys.map((k) => [k, null]));

/**
 * The active filters as chips, except the two with controls of their own: status (the tabs) and
 * time (its own chip), and "latest", which is a switch beside the tabs.
 */
export function filterChips(sp: URLSearchParams, lookups: ChipLookups): FilterChip[] {
  const chips: FilterChip[] = [];
  const negated = negatedFilters(sp);
  // The two readings of a filter turned round by `not`, and which it is now.
  const turn = (kind: NegatableExchangeFilter, ops: [string, string], hint?: string) => {
    const now = negated.includes(kind);
    return {
      op: ops[now ? 1 : 0],
      turn: { negated: now, ops, hint, set: (to: boolean): ParamChanges => ({ not: notWith(sp, kind, to) }) },
    };
  };

  // One picked reads by its name; several, by how many, with the names on hover.
  const picked = (
    kind: "subscription" | "partner" | "informationType",
    key: string,
    name: string,
    plural: string,
    nameOf: (id: number) => string | undefined,
    hint?: string,
  ) => {
    const list = idList(sp.get(key));
    if (list.length === 0) return;
    const names = list.map((id) => nameOf(id) ?? `#${id}`);
    chips.push({
      id: kind,
      kind,
      name,
      field: name,
      value: list.length === 1 ? names[0] : `${list.length} ${plural}`,
      title: list.length === 1 ? undefined : names.join(", "),
      remove: drop(key),
      ...turn(kind, list.length === 1 ? ["is", "is not"] : ["is any of", "is none of"], hint),
    });
  };
  picked("subscription", "subscriptionId", "Subscription", "subscriptions", lookups.subscriptionName, "Also keeps exchanges with no subscription.");
  picked("partner", "partnerId", "Partner", "partners", lookups.partnerName, "Also keeps exchanges with no partner.");
  picked("informationType", "informationTypeId", "Information type", "information types", lookups.informationTypeName);

  // One chip per promoted property, each removed and turned round on its own.
  const properties = propertyFilters(sp);
  properties.forEach((p, index) => {
    const replaced = (by: ExchangePropertyFilter | null) =>
      withPropertyFilters(sp, properties.flatMap((q, i) => (i !== index ? [q] : by ? [by] : [])));
    const reads = (ops: [string, string], hint?: string) => ({
      op: ops[p.negated ? 1 : 0],
      turn: { negated: p.negated, ops, hint, set: (to: boolean) => replaced({ ...p, negated: to }) },
    });
    const base = { id: `property-${index}`, kind: "property" as const, index, remove: replaced(null) };
    chips.push(
      p.key && p.text
        ? { ...base, name: p.key, field: p.key, value: p.text, mono: true, ...reads(["contains", "does not contain"], `Also keeps exchanges without ${p.key}.`) }
        : p.key
          ? { ...base, name: p.key, field: p.key, value: "present", ...reads(["is", "is not"]) }
          : { ...base, name: "Properties", field: "Properties", value: p.text!, mono: true, ...reads(["contain", "do not contain"], "Also keeps exchanges that promoted nothing.") },
    );
  });

  const ids = sp.get("ids");
  if (ids) {
    const list = ids.split(/[\s,|]+/).filter(Boolean);
    chips.push(
      list.length === 1
        ? { id: "ids", kind: "ids", name: "Exchange ids", field: "Exchange", op: "is", value: list[0], mono: true, remove: drop("ids") }
        : { id: "ids", kind: "ids", name: "Exchange ids", field: "Exchange", op: "is any of", value: `${list.length} ids`, remove: drop("ids") },
    );
  }
  const correlation = sp.get("correlationId");
  if (correlation)
    chips.push({ id: "correlationId", kind: "correlationId", name: "Correlation id", field: "Correlation id", op: "is", value: correlation, mono: true, remove: drop("correlationId") });
  const run = sp.get("receiveAttemptId");
  if (run) chips.push({ id: "receiveAttempt", kind: "receiveAttempt", name: "Created by run", field: "Created by run", op: "", value: `#${run}`, remove: drop("receiveAttemptId") });
  const error = sp.get("error");
  if (error)
    chips.push({ id: "error", kind: "error", name: "Error", field: "Error", value: error, mono: true, remove: drop("error"), ...turn("error", ["contains", "does not contain"], "Also keeps exchanges with no error.") });
  if (sp.get("retry") === "1")
    chips.push({ id: "retry", kind: "retry", name: "Is a retry", field: "Exchange", value: "a retry", remove: drop("retry"), ...turn("retry", ["is", "is not"]) });
  if (sp.get("scheduled") === "1")
    chips.push({ id: "scheduled", kind: "scheduled", name: "Auto-retry scheduled", field: "Auto-retry", value: "scheduled", remove: drop("scheduled"), ...turn("scheduled", ["is", "is not"]) });
  if (sp.get("aggregated") === "1")
    chips.push({ id: "aggregated", kind: "aggregated", name: "Part of an aggregation", field: "Exchange", value: "part of an aggregation", remove: drop("aggregated"), ...turn("aggregated", ["is", "is not"]) });
  return chips;
}

// --- the search box ---

const GUID = /^[0-9a-f]{8}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{12}$/i;

/** Exchange ids in text, when the text is nothing but ids (one or many, any separator). */
export function idsIn(text: string): string[] | null {
  const parts = text.split(/[\s,|;]+/).filter(Boolean);
  return parts.length > 0 && parts.every((p) => GUID.test(p)) ? parts : null;
}

export interface SearchSuggestion {
  key: string;
  label: string;
  value: string;
  hint: string;
  changes: ParamChanges;
}

/**
 * What a typed search could mean, most likely first. One box instead of a field per kind of
 * value: someone holding an order number rarely knows whether Bitween calls it a promoted
 * property, a correlation id or the text of an error.
 */
export function searchSuggestions(text: string, sp: URLSearchParams = new URLSearchParams()): SearchSuggestion[] {
  const value = text.trim();
  if (!value) return [];

  const ids = idsIn(value);
  if (ids)
    return [
      {
        key: "ids",
        label: ids.length === 1 ? "Exchange" : `${ids.length} exchanges`,
        value: ids.length === 1 ? ids[0] : ids.slice(0, 2).join(", ") + (ids.length > 2 ? ", ..." : ""),
        hint: "Opens it, with its retries and aggregation family",
        changes: { ids: ids.join(","), open: ids.length === 1 ? ids[0] : null },
      },
      { key: "correlationId", label: "Correlation id is", value, hint: "Every exchange in the same flow", changes: { correlationId: value } },
    ];

  // "key:value" is how promoted properties are stored, so it is also how one is asked for.
  const keyed = value.match(/^([^:\s][^:]*):(.*)$/);
  const suggestions: SearchSuggestion[] = [];
  if (keyed)
    suggestions.push({
      key: "keyed",
      label: `Property ${keyed[1]} contains`,
      value: keyed[2].trim(),
      hint: "Promoted property, by name",
      // Added beside any properties already filtered on, not instead of them.
      changes: withPropertyFilters(sp, [...propertyFilters(sp), { key: keyed[1].trim(), text: keyed[2].trim() || null, negated: false }]),
    });
  suggestions.push(
    {
      key: "property",
      label: "Any property contains",
      value,
      hint: "Across every promoted property",
      changes: withPropertyFilters(sp, [...propertyFilters(sp).filter((p) => p.key), { key: null, text: value, negated: false }]),
    },
    { key: "correlationId", label: "Correlation id is", value, hint: "Every exchange in the same flow", changes: { correlationId: value } },
    { key: "error", label: "Error message contains", value, hint: "Searches the exception text of failed exchanges", changes: { error: value } },
  );
  return suggestions;
}

// --- saved views ---

export interface SavedView {
  name: string;
  /** The filters as a query string, without paging or the open row. */
  search: string;
  builtIn?: boolean;
  description?: string;
}

/** Views every installation starts with: the questions people come to this page to ask. */
export const BUILT_IN_VIEWS: SavedView[] = [
  { name: "All exchanges", search: "", builtIn: true, description: "No filters" },
  { name: "Needs attention", search: "status=failed&latest=1", builtIn: true, description: "Failed, newest attempt of each chain" },
  { name: "Bad responses", search: "status=badResponse&latest=1", builtIn: true, description: "Delivered, but the reply reported an error" },
  { name: "Auto-retry scheduled", search: "scheduled=1", builtIn: true, description: "Waiting on their retry policy" },
  { name: "Still processing", search: "status=processing", builtIn: true, description: "Not finished yet" },
];

/** Just the filters, in the order FILTER_KEYS lists them, for saving and comparing views. */
export function viewSearch(sp: URLSearchParams): string {
  const out = new URLSearchParams();
  for (const k of FILTER_KEYS) for (const v of sp.getAll(k)) out.append(k, v);
  return out.toString();
}
