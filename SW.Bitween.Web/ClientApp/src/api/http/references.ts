/**
 * Reference tokens inside adapter property values.
 *
 * Adapters can carry `{{globals.<set>.<key>}}` and `{{partner.<key>}}` in any
 * property value; the backend substitutes them at run time
 * (`Helpers/StartupValuesFiller.cs`). The subscription list withholds the
 * property values, since they hold credentials, so the backend reads the tokens
 * and the list carries what they name in each row's `references`.
 *
 * Ids and keys come back as written, and the resolver looks them up with
 * `OrdinalIgnoreCase` - which is why callers must compare them
 * case-insensitively.
 */
export interface ReferenceTokens {
  /** Global value references, grouped by the set id as written in the token. */
  globals: { setId: string; keys: string[] }[];
  /** Partner property keys referenced by `{{partner.KEY}}`. */
  partnerPropKeys: string[];
}

/** A subscription list row's `references`, as `Subscriptions/Search` sends it. */
export interface RawReferences {
  globals?: { setId: string; keys: string[] | null }[] | null;
  partnerProperties?: string[] | null;
}

export const referencesFrom = (raw: RawReferences | null | undefined): ReferenceTokens => ({
  globals: (raw?.globals ?? []).map((g) => ({ setId: g.setId, keys: g.keys ?? [] })),
  partnerPropKeys: raw?.partnerProperties ?? [],
});

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
