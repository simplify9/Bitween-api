import { ApiRequestError } from "../types";

/**
 * All endpoints live under /bitween/api (UrlPrefix="api" + UsePathBase("/bitween")).
 * The SPA is served from the same origin, so this stays relative and cookies flow.
 */
export const API_BASE = "/bitween/api";

/** The Jwt is kept in localStorage; the refresh token is an HttpOnly cookie JS never sees. */
const TOKEN_KEY = "access_token";

export const getToken = (): string | null => localStorage.getItem(TOKEN_KEY);
export const setToken = (jwt: string): void => localStorage.setItem(TOKEN_KEY, jwt);
export const clearToken = (): void => localStorage.removeItem(TOKEN_KEY);

/** Backend serializes camelCase; login returns `{ jwt }`. */
const readJwt = (data: unknown): string | null =>
  (data as { jwt?: string; Jwt?: string })?.jwt ?? (data as { Jwt?: string })?.Jwt ?? null;

let refreshInFlight: Promise<string | null> | null = null;

/**
 * Silent refresh: POST /accounts/login with an empty body — the HttpOnly
 * refresh_token cookie alone re-issues a Jwt. Returns the new token, or null
 * when the cookie is missing/expired. Bypasses `request()` to avoid recursion,
 * and dedupes concurrent callers behind one in-flight promise.
 */
export function silentRefresh(): Promise<string | null> {
  if (!refreshInFlight) {
    refreshInFlight = (async () => {
      try {
        const res = await fetch(`${API_BASE}/accounts/login`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          credentials: "include",
          body: "{}",
        });
        if (!res.ok) return null;
        const jwt = readJwt(await res.json().catch(() => null));
        if (jwt) setToken(jwt);
        else clearToken();
        return jwt;
      } catch {
        return null;
      }
    })();
    void refreshInFlight.finally(() => {
      refreshInFlight = null;
    });
  }
  return refreshInFlight;
}

/** Pull a human message + best-effort code out of a backend error response. */
async function toApiError(res: Response): Promise<ApiRequestError> {
  const text = await res.text().catch(() => "");
  let body: unknown = text;
  try {
    body = text ? JSON.parse(text) : "";
  } catch {
    /* leave as text */
  }

  // 404 → the message is a bare JSON string.
  if (typeof body === "string" && body)
    return new ApiRequestError(res.status === 404 ? "NOT_FOUND" : "ERROR", body);

  // Framework-level errors (415, unhandled 500, …) come as ASP.NET ProblemDetails:
  // { type, title, status, traceId }. Prefer its human `title`.
  if (body && typeof body === "object" && "title" in body && "status" in body) {
    const pd = body as { title?: string; status?: number };
    return new ApiRequestError(`HTTP_${pd.status ?? res.status}`, pd.title || `Request failed (${res.status}).`);
  }

  // 400 → ASP.NET SerializableError: { key: [msg, ...] } (key is the validation
  // code for SWValidationException, or the exception type name otherwise).
  if (body && typeof body === "object") {
    const [code, value] = Object.entries(body as Record<string, unknown>)[0] ?? [];
    const message = Array.isArray(value) ? String(value[0]) : String(value ?? "");
    if (code) return new ApiRequestError(code, message || "Request failed.");
  }

  return new ApiRequestError("ERROR", `Request failed (${res.status}).`);
}

export interface RequestOptions {
  method?: "GET" | "POST" | "DELETE";
  body?: unknown;
  /** Internal: prevents the 401 → refresh → retry loop from recursing. */
  _retried?: boolean;
}

/**
 * The one fetch helper every wired method goes through: prefixes the base,
 * attaches `Authorization: Bearer <jwt>`, includes credentials so the refresh
 * cookie rides along, and on 401 attempts a single silent refresh + retry.
 */
export async function request<T>(path: string, opts: RequestOptions = {}): Promise<T> {
  const token = getToken();
  const method = opts.method ?? "GET";
  // Backend command handlers (POST) bind a JSON body, so they always need
  // `Content-Type: application/json` — even a body-less command like logout.
  // Without it the framework rejects the call with 415 before the handler runs;
  // an empty `{}` satisfies it.
  const sendJson = method === "POST";
  const res = await fetch(`${API_BASE}${path}`, {
    method,
    credentials: "include",
    headers: {
      ...(sendJson ? { "Content-Type": "application/json" } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: sendJson ? JSON.stringify(opts.body ?? {}) : undefined,
  });

  if (res.status === 401 && !opts._retried) {
    const refreshed = await silentRefresh();
    if (refreshed) return request<T>(path, { ...opts, _retried: true });
    clearToken();
    throw new ApiRequestError("UNAUTHENTICATED", "Your session has ended. Please sign in again.");
  }

  if (!res.ok) throw await toApiError(res);

  if (res.status === 204) return undefined as T;
  const text = await res.text();
  if (!text) return undefined as T;
  // Some endpoints return a bare string as text/plain (e.g. /partners/generatekey),
  // which isn't valid JSON — only parse when the response actually is JSON.
  const isJson = res.headers.get("content-type")?.includes("application/json") ?? false;
  return (isJson ? JSON.parse(text) : text) as T;
}

export const get = <T>(path: string): Promise<T> => request<T>(path);
export const post = <T>(path: string, body?: unknown): Promise<T> =>
  request<T>(path, { method: "POST", body });

/**
 * For a secondary read that only enriches a page — the "used by" counts, which come from another
 * area's list. Those are permission-guarded in their own right, so a role that can see this page
 * but not that area would otherwise take the whole page down with it. The enrichment is worth
 * losing; the page isn't. Only refusals are swallowed, so a real outage still surfaces.
 */
export async function getEnrichment<T>(path: string, fallback: T): Promise<T> {
  try {
    return await get<T>(path);
  } catch (e) {
    // A refusal for *this* read only. UNAUTHENTICATED deliberately isn't swallowed: that one means
    // the session itself is gone, and the app needs to hear about it.
    const code = e instanceof ApiRequestError ? e.code : "";
    if (code === "HTTP_401" || code === "HTTP_403") return fallback;
    throw e;
  }
}
