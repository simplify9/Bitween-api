/**
 * Builds a Searchy query string — the `filter=Field:Rule:Value` + `page`/`size`
 * convention every list endpoint in this API already speaks (see `buildExchangeQuery`
 * for the hand-written original this generalizes).
 *
 * `page` and `size` are always sent together, never one without the other: at least
 * one backend handler (`Subscriptions/Search.cs`, filtering by name) diverts a text
 * filter through an in-memory `Skip(size * page).Take(size)` with no "size == 0 means
 * unbounded" guard, so an omitted size silently returns zero rows despite a correct
 * total count. Always supplying both sidesteps that regardless of which handler runs.
 */
/**
 * Serializes a Searchy query string, percent-encoding spaces.
 *
 * `URLSearchParams.toString()` form-encodes a space as "+", but the backend parses this query
 * string with `Uri.UnescapeDataString` (SW.PrimitiveTypes' QueryStringParser), which decodes
 * %XX escapes and leaves "+" as a literal plus. So every multi-word term silently matched
 * nothing: searching "Order intake" looked for "Order+intake". Only `filter=` is affected —
 * endpoints whose parameters are model-bound go through ASP.NET's own parser, which reads "+"
 * as a space correctly.
 *
 * Safe as a blanket replace: URLSearchParams already encodes a literal "+" as %2B, so any bare
 * "+" left in the output is a space.
 */
export const searchyQueryString = (params: URLSearchParams): string =>
  params.toString().replace(/\+/g, "%20");

export function buildListQuery(opts: {
  /** [Field, Rule, Value] triples — Rule 1 = EqualsTo, 4 = Contains. Skipped when Value is "". */
  filters?: [string, number, string | number][];
  /** [Field, Order] — Order 1 = ascending, 2 = descending. */
  sort?: [string, number];
  offset: number;
  limit: number;
}): string {
  const params = new URLSearchParams();
  for (const [field, rule, value] of opts.filters ?? []) {
    if (value !== "" && value !== undefined && value !== null) params.append("filter", `${field}:${rule}:${value}`);
  }
  if (opts.sort) params.append("sort", `${opts.sort[0]}:${opts.sort[1]}`);
  params.set("page", String(Math.floor(opts.offset / opts.limit)));
  params.set("size", String(opts.limit));
  return searchyQueryString(params);
}

export const SEARCHY_RULE = {
  equalsTo: 1,
  contains: 4,
} as const;

export const SEARCHY_SORT = {
  asc: 1,
  desc: 2,
} as const;
