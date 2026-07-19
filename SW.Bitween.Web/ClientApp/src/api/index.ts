import type { ApiClient } from "./client";
import { httpClient } from "./http/httpClient";

/**
 * The swap point. Everything in the UI imports `api` from here.
 * This is the real HTTP client — no mock, no toggle. Domains not yet wired
 * reject with NotWiredError so their screens read as honestly-not-connected
 * (see BACKEND_WIRING_PLAN §2).
 */
export const api: ApiClient = httpClient;

export { getAppConfig } from "./http/appConfig";
export type { AppConfig } from "./http/appConfig";
export * from "./types";
