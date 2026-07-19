import type { ApiClient } from "../client";
import { NotWiredError } from "../types";
import { partnerMethods } from "./partners";
import { sessionMethods } from "./session";

/**
 * The single real client. Wired domains are merged in here; every other
 * ApiClient method resolves to a rejected NotWiredError so its screen shows an
 * honest "Not connected yet" state instead of fake data. Each batch adds its
 * domain module to `wired` (see BACKEND_WIRING_PLAN §5–6).
 */
const wired: Partial<ApiClient> = {
  ...sessionMethods,
  ...partnerMethods,
};

export const httpClient: ApiClient = new Proxy(wired, {
  get(target, prop, receiver) {
    if (typeof prop !== "string" || prop in target) return Reflect.get(target, prop, receiver);
    // Anything not yet wired: a callable that rejects clearly, so `await api.x()` fails honestly.
    return () => Promise.reject(new NotWiredError(prop));
  },
}) as ApiClient;
