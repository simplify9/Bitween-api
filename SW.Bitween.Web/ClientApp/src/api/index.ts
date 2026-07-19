import type { ApiClient } from "./client";
import { mockClient } from "./mock/mockClient";

/**
 * The swap point. Everything in the UI imports `api` from here.
 * When the backend is ready, replace `mockClient` with an HTTP
 * implementation of `ApiClient` — no component changes needed.
 */
export const api: ApiClient = mockClient;

export * from "./types";
