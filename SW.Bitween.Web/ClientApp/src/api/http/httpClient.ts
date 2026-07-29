import type { ApiClient } from "../client";
import { NotWiredError } from "../types";
import { adapterMethods } from "./adapters";
import { dashboardMethods } from "./dashboard";
import { documentMethods } from "./documents";
import { exchangeMethods } from "./exchanges";
import { gatewayMethods } from "./gateways";
import { globalValuesMethods } from "./globalValues";
import { integrationMethods } from "./integrations";
import { mapperMethods } from "./mappers";
import { notifierMethods } from "./notifiers";
import { partnerMethods } from "./partners";
import { queueHealthMethods } from "./queueHealth";
import { retryPolicyMethods } from "./retryPolicies";
import { sessionMethods } from "./session";
import { settingsMethods } from "./settings";
import { teamMethods } from "./team";
import { workGroupMethods } from "./workGroups";

/**
 * The single real client. Wired domains are merged in here; every other
 * ApiClient method resolves to a rejected NotWiredError so its screen shows an
 * honest "Not connected yet" state instead of fake data. Each batch adds its
 * domain module to `wired` (see BACKEND_WIRING_PLAN §5–6).
 */
const wired: Partial<ApiClient> = {
  ...sessionMethods,
  ...partnerMethods,
  ...documentMethods,
  ...globalValuesMethods,
  ...workGroupMethods,
  ...retryPolicyMethods,
  ...integrationMethods,
  ...adapterMethods,
  ...gatewayMethods,
  ...exchangeMethods,
  ...queueHealthMethods,
  ...dashboardMethods,
  ...mapperMethods,
  ...notifierMethods,
  ...teamMethods,
  ...settingsMethods,
};

export const httpClient: ApiClient = new Proxy(wired, {
  get(target, prop, receiver) {
    if (typeof prop !== "string" || prop in target) return Reflect.get(target, prop, receiver);
    // Anything not yet wired: a callable that rejects clearly, so `await api.x()` fails honestly.
    return () => Promise.reject(new NotWiredError(prop));
  },
}) as ApiClient;
