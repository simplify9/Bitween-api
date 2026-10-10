import type { ApiClient } from "./client";
import { httpClient } from "./http/httpClient";

/**
 * The swap point. Everything in the UI imports `api` from here.
 * This is the real HTTP client — no mock, no toggle.
 */
export const api: ApiClient = httpClient;

export { getAppConfig, resetAppConfig } from "./http/appConfig";
export { grantCliSignIn } from "./http/cliSignIn";
export type { AppConfig } from "./http/appConfig";
export { referencesGlobal, referencesPartnerProp } from "./http/references";
export { onSessionEnded, onSignedOutElsewhere } from "./http/request";
export * from "./types";
