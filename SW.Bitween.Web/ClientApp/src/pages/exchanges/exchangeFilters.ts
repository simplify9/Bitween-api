import type { ExchangeQuery, ExchangeStatus } from "../../api";

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
  "error",
  "retry",
  "scheduled",
  "aggregated",
  "range",
  "from",
  "to",
  "latest",
] as const;

export type FilterKey = (typeof FILTER_KEYS)[number];

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

/** The list's query, as of `now` - which only matters for a relative `range`. */
export function readQuery(sp: URLSearchParams, now: Date = new Date()): ExchangeQuery {
  const range = sp.get("range");
  const resolved = range ? resolveRange(range, now) : null;
  const from = resolved ? resolved.from : parseBound(sp.get("from"), "from");
  const to = resolved ? resolved.to : parseBound(sp.get("to"), "to");
  const status = sp.get("status");

  return {
    status: STATUSES.includes(status as ExchangeStatus) ? (status as ExchangeStatus) : undefined,
    subscriptionId: num(sp.get("subscriptionId")),
    partnerId: num(sp.get("partnerId")),
    informationTypeId: num(sp.get("informationTypeId")),
    ids: sp.get("ids") ?? undefined,
    receiveAttemptId: num(sp.get("receiveAttemptId")),
    correlationId: sp.get("correlationId") ?? undefined,
    latest: flag(sp.get("latest")),
    propertyKey: sp.get("propertyKey") ?? undefined,
    property: sp.get("property") ?? undefined,
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
  changes: Record<string, string | null | undefined>,
  { resetOffset = true }: { resetOffset?: boolean } = {},
): URLSearchParams {
  const next = new URLSearchParams(sp);
  for (const [key, value] of Object.entries(changes)) {
    if (value === null || value === undefined || value === "") next.delete(key);
    else next.set(key, value);
  }
  if (resetOffset) next.delete("offset");
  return next;
}

/** The filters alone, in a fixed order, so two URLs that select the same rows compare equal. */
export const filterSignature = (sp: URLSearchParams) => FILTER_KEYS.map((k) => sp.get(k) ?? "").join("\u0000");

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
  kind: FilterKind;
  field: string;
  op: string;
  value: string;
  mono?: boolean;
  /** The parameters removing it drops. */
  keys: string[];
}

export interface ChipLookups {
  subscriptionName: (id: number) => string | undefined;
  partnerName: (id: number) => string | undefined;
  informationTypeName: (id: number) => string | undefined;
}

/**
 * The active filters as chips, except the two with controls of their own: status (the tabs) and
 * time (its own chip), and "latest", which is a switch beside the tabs.
 */
export function filterChips(sp: URLSearchParams, lookups: ChipLookups): FilterChip[] {
  const chips: FilterChip[] = [];
  const id = (key: string) => num(sp.get(key));

  const sub = id("subscriptionId");
  if (sub !== undefined)
    chips.push({ kind: "subscription", field: "Subscription", op: "is", value: lookups.subscriptionName(sub) ?? `#${sub}`, keys: ["subscriptionId"] });
  const partner = id("partnerId");
  if (partner !== undefined)
    chips.push({ kind: "partner", field: "Partner", op: "is", value: lookups.partnerName(partner) ?? `#${partner}`, keys: ["partnerId"] });
  const type = id("informationTypeId");
  if (type !== undefined)
    chips.push({ kind: "informationType", field: "Information type", op: "is", value: lookups.informationTypeName(type) ?? `#${type}`, keys: ["informationTypeId"] });

  const key = sp.get("propertyKey");
  const property = sp.get("property");
  if (key || property)
    chips.push(
      key && property
        ? { kind: "property", field: key, op: "contains", value: property, mono: true, keys: ["propertyKey", "property"] }
        : key
          ? { kind: "property", field: key, op: "is", value: "present", keys: ["propertyKey", "property"] }
          : { kind: "property", field: "Any property", op: "contains", value: property!, mono: true, keys: ["propertyKey", "property"] },
    );

  const ids = sp.get("ids");
  if (ids) {
    const list = ids.split(/[\s,|]+/).filter(Boolean);
    chips.push(
      list.length === 1
        ? { kind: "ids", field: "Exchange", op: "is", value: list[0], mono: true, keys: ["ids"] }
        : { kind: "ids", field: "Exchange", op: "is any of", value: `${list.length} ids`, keys: ["ids"] },
    );
  }
  const correlation = sp.get("correlationId");
  if (correlation) chips.push({ kind: "correlationId", field: "Correlation id", op: "is", value: correlation, mono: true, keys: ["correlationId"] });
  const run = sp.get("receiveAttemptId");
  if (run) chips.push({ kind: "receiveAttempt", field: "Created by run", op: "", value: `#${run}`, keys: ["receiveAttemptId"] });
  const error = sp.get("error");
  if (error) chips.push({ kind: "error", field: "Error", op: "contains", value: error, mono: true, keys: ["error"] });
  if (sp.get("retry") === "1") chips.push({ kind: "retry", field: "Is a retry", op: "", value: "", keys: ["retry"] });
  if (sp.get("scheduled") === "1") chips.push({ kind: "scheduled", field: "Auto-retry scheduled", op: "", value: "", keys: ["scheduled"] });
  if (sp.get("aggregated") === "1") chips.push({ kind: "aggregated", field: "Part of an aggregation", op: "", value: "", keys: ["aggregated"] });
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
  changes: Record<string, string | null>;
}

/**
 * What a typed search could mean, most likely first. One box instead of a field per kind of
 * value: someone holding an order number rarely knows whether Bitween calls it a promoted
 * property, a correlation id or the text of an error.
 */
export function searchSuggestions(text: string): SearchSuggestion[] {
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
      changes: { propertyKey: keyed[1].trim(), property: keyed[2].trim() || null },
    });
  suggestions.push(
    { key: "property", label: "Any property contains", value, hint: "Across every promoted property", changes: { property: value, propertyKey: null } },
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
  for (const k of FILTER_KEYS) {
    const v = sp.get(k);
    if (v !== null) out.set(k, v);
  }
  return out.toString();
}
