import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { api, type GlobalValuesSetRow } from "../../api";
import type { KeyValuePair } from "../../lib/mapping/types";
import type { ValuesSetMap } from "../../lib/mapping/scribanGenerator";

/** Boundary adapters: the prototype persists mapperProperties as Record<string,string>,
 *  while the verbatim mapping reducer/generator speak the legacy KeyValuePair[] shape. */
export const recordToKvps = (rec: Record<string, string>): KeyValuePair[] =>
  Object.entries(rec).map(([key, value]) => ({ key, value }));

export const kvpsToRecord = (kvps: KeyValuePair[]): Record<string, string> =>
  Object.fromEntries(kvps.map((p) => [p.key, p.value]));

/**
 * Prototype-native replacements for the legacy RTK data hooks the mapping editor
 * used (`useGlobalAdapterValuesSetsQuery`, `useValuesSetMap`). The legacy
 * `GlobalAdapterValuesSetModel` was `{ id, name, values }` — a structural subset
 * of the prototype's `GlobalValuesSetRow`, so consumers are unchanged apart from
 * the source of the data.
 */
export function useGlobalSets(): GlobalValuesSetRow[] {
  const { data } = useQuery({ queryKey: ["value-sets"], queryFn: () => api.listValueSets() });
  return data ?? [];
}

/** Flat lookup map consumed by the Scriban generator (template gen on save + preview). */
export function useValuesSetMap(): ValuesSetMap {
  const sets = useGlobalSets();
  return useMemo(() => {
    const map: ValuesSetMap = {};
    for (const s of sets) map[s.id] = s.values;
    return map;
  }, [sets]);
}
