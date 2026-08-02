/**
 * Reference tokens inside adapter property values.
 *
 * Adapters can carry `{{globals.<set>.<key>}}` and `{{partner.<key>}}` in any
 * property value; the backend substitutes them at run time
 * (`Helpers/StartupValuesFiller.cs`). Nothing indexes them, so "who references
 * this value set / partner property?" can only be answered by scanning the
 * properties the same way the resolver parses them.
 *
 * The resolver splits the globals token on the **first** "." only and puts no
 * character-class restriction on either half, so these patterns are equally
 * permissive rather than assuming a slug charset. It also matches the prefix
 * and looks up ids and keys with `OrdinalIgnoreCase` — hence the `i` flag, and
 * why callers must compare what comes back case-insensitively.
 */
const GLOBAL_TOKEN_RE = /\{\{globals\.([^.]+)\.([^}]+)\}\}/gi;
const PARTNER_TOKEN_RE = /\{\{partner\.([^}]+)\}\}/gi;

export interface ReferenceTokens {
  /** Global value references, grouped by the set id as written in the token. */
  globals: { setId: string; keys: string[] }[];
  /** Partner property keys referenced by `{{partner.KEY}}`. */
  partnerPropKeys: string[];
}

export function scanReferenceTokens(values: (string | null | undefined)[]): ReferenceTokens {
  const globals = new Map<string, Set<string>>();
  const partnerPropKeys = new Set<string>();

  for (const value of values) {
    if (!value) continue;
    for (const [, setId, key] of value.matchAll(GLOBAL_TOKEN_RE)) {
      const keys = globals.get(setId) ?? new Set<string>();
      keys.add(key);
      globals.set(setId, keys);
    }
    for (const [, key] of value.matchAll(PARTNER_TOKEN_RE)) partnerPropKeys.add(key);
  }

  return {
    globals: [...globals].map(([setId, keys]) => ({ setId, keys: [...keys] })),
    partnerPropKeys: [...partnerPropKeys],
  };
}

/** Case-insensitive, matching how the resolver compares set ids and keys. */
const eq = (a: string, b: string) => a.localeCompare(b, undefined, { sensitivity: "accent" }) === 0;

export const referencesGlobal = (
  refs: Pick<ReferenceTokens, "globals">,
  setId: string,
  key?: string,
): boolean =>
  refs.globals.some(
    (g) => eq(g.setId, setId) && (key === undefined || g.keys.some((k) => eq(k, key))),
  );

export const referencesPartnerProp = (refs: Pick<ReferenceTokens, "partnerPropKeys">, key: string): boolean =>
  refs.partnerPropKeys.some((k) => eq(k, key));
