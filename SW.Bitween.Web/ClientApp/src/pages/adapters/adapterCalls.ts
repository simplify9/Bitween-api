import { createContext, useContext } from "react";
import type { AdapterUsage as AdapterCalls } from "../../api/metricsTypes";

/** The window the Adapters page counts calls over. */
export const CALL_DAYS = 7;

export interface AdapterCallsByAdapter {
  /** Lower-case adapter id to its calls in the window. */
  calls: Map<string, AdapterCalls>;
  /** Lower-case adapter id to its last call in the last 30 days, called in the window or not. */
  lastCalled: Map<string, string>;
  /** When call counting began on this instance; null before any. */
  since: string | null;
  loaded: boolean;
}

export const AdapterCallsContext = createContext<AdapterCallsByAdapter>({
  calls: new Map(),
  lastCalled: new Map(),
  since: null,
  loaded: false,
});

export const useAdapterCalls = (adapterId: string) => {
  const all = useContext(AdapterCallsContext);
  const id = adapterId.toLowerCase();
  return { calls: all.calls.get(id), lastCalled: all.lastCalled.get(id) ?? null, since: all.since, loaded: all.loaded };
};
