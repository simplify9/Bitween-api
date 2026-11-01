import { ApiRequestError } from "../types";
import { carriedOn, currentPage, resetBackground, takeShown, WAIT_SECONDS, type BackgroundStatus } from "./background";

/**
 * All endpoints live under /api (UrlPrefix="api").
 * The SPA is served from the same origin, so this stays relative and cookies flow.
 */
export const API_BASE = "/api";

/**
 * Where earlier versions kept the Jwt. Read once at load only to delete it, so a token left
 * behind by an older build does not outlive the upgrade.
 */
export const TOKEN_KEY = "access_token";

/**
 * Set while this browser holds a session, cleared when it ends. Not a credential - it only tells
 * a new tab or a reload that a refresh cookie is worth trying, so the sign-in page doesn't spend
 * the sign-in rate limit probing for a session that was never there.
 */
export const SIGNED_IN_KEY = "bitween_signed_in";

/**
 * The Jwt lives in this module's memory and nowhere else; the refresh token is an HttpOnly
 * cookie JS never sees. localStorage was readable by any script that ever ran on the page, and
 * a stolen Jwt works from anywhere until it expires. A reload starts with no Jwt and gets one
 * from the refresh cookie (see `getSession`).
 */
let accessToken: string | null = null;

type SessionMessage = { type: "token"; jwt: string } | { type: "signed-out" };

/**
 * How tabs share what used to be shared through localStorage: a new Jwt, and the end of the
 * session. Missing in very old browsers, where each tab simply refreshes on its own.
 */
const channel: BroadcastChannel | null =
  typeof BroadcastChannel === "undefined" ? null : new BroadcastChannel("bitween-session");

// Node (the test runner) keeps a process alive while a channel listens; browsers have no unref.
(channel as unknown as { unref?: () => void } | null)?.unref?.();

let signedOutElsewhereListener: (() => void) | null = null;

/** Told when another tab of this origin ends the session. `SessionProvider` registers itself. */
export const onSignedOutElsewhere = (listener: (() => void) | null): void => {
  signedOutElsewhereListener = listener;
};

channel?.addEventListener("message", (event: MessageEvent<SessionMessage>) => {
  if (event.data?.type === "token") accessToken = event.data.jwt;
  else if (event.data?.type === "signed-out") {
    accessToken = null;
    signedOutElsewhereListener?.();
  }
});

const safeStorage = (action: () => void): void => {
  try {
    action();
  } catch {
    /* storage can be unavailable (private mode, blocked site data); the hint is only an optimisation */
  }
};

safeStorage(() => localStorage.removeItem(TOKEN_KEY));

/**
 * Told when a request proves the session is over - the Jwt was refused and no
 * refresh cookie was left to replace it.
 *
 * A callback rather than a hook because this is plain module code that cannot
 * reach React state. `SessionProvider` registers itself on mount; with nothing
 * registered, the `UNAUTHENTICATED` error below is all that happens, which is
 * what used to leave a dead session rendering the entire app until somebody
 * pressed refresh.
 */
let sessionEndedListener: (() => void) | null = null;
export const onSessionEnded = (listener: (() => void) | null): void => {
  sessionEndedListener = listener;
};

export const getToken = (): string | null => accessToken;

export const setToken = (jwt: string): void => {
  accessToken = jwt;
  safeStorage(() => localStorage.setItem(SIGNED_IN_KEY, "1"));
  channel?.postMessage({ type: "token", jwt } satisfies SessionMessage);
};

export const clearToken = (): void => {
  accessToken = null;
  resetBackground();
  safeStorage(() => localStorage.removeItem(SIGNED_IN_KEY));
  channel?.postMessage({ type: "signed-out" } satisfies SessionMessage);
};

/** Whether this browser held a session when it last looked - worth a silent refresh to find out. */
export const mayHaveSession = (): boolean => {
  try {
    return localStorage.getItem(SIGNED_IN_KEY) === "1";
  } catch {
    return true;
  }
};

/** Backend serializes camelCase; login returns `{ jwt }`. */
const readJwt = (data: unknown): string | null =>
  (data as { jwt?: string; Jwt?: string })?.jwt ?? (data as { Jwt?: string })?.Jwt ?? null;

let refreshInFlight: Promise<string | null> | null = null;

/**
 * Silent refresh: POST /accounts/login with an empty body - the HttpOnly
 * refresh_token cookie alone re-issues a Jwt. Returns the new token, or null
 * when the cookie is missing/expired. Bypasses `request()` to avoid recursion,
 * and dedupes concurrent callers behind one in-flight promise.
 *
 * Serialised across tabs with a Web Lock. Each refresh replaces the cookie and
 * deletes the token it was sent with, so two tabs refreshing at once would send
 * the same cookie and the second would be refused - signing that tab out. Behind
 * the lock the second tab sends the cookie the first one just got, or, when the
 * first tab's new Jwt has already arrived over the channel, does not refresh at all.
 */
export function silentRefresh(): Promise<string | null> {
  if (!refreshInFlight) {
    const tokenBefore = accessToken;
    const refresh = async (): Promise<string | null> => {
      if (accessToken && accessToken !== tokenBefore) return accessToken;
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
    };
    refreshInFlight = (async () =>
      typeof navigator !== "undefined" && navigator.locks
        ? navigator.locks.request("bitween-refresh", refresh)
        : refresh())();
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
    return new ApiRequestError(res.status === 404 ? "NOT_FOUND" : "ERROR", body, res.status);

  // Framework-level errors (415, unhandled 500, …) come as ASP.NET ProblemDetails:
  // { type, title, status, traceId }. Prefer its human `title`.
  if (body && typeof body === "object" && "title" in body && "status" in body) {
    const pd = body as { title?: string; status?: number };
    return new ApiRequestError(`HTTP_${pd.status ?? res.status}`, pd.title || `Request failed (${res.status}).`, res.status);
  }

  // 400 → ASP.NET SerializableError: { key: [msg, ...] } (key is the validation
  // code for SWValidationException, or the exception type name otherwise).
  // Every message is kept: the first key's first message used to be all that reached the form,
  // so a save with two problems showed one, and the second only after fixing the first.
  if (body && typeof body === "object") {
    const errors: Record<string, string[]> = {};
    for (const [key, value] of Object.entries(body as Record<string, unknown>))
      errors[key] = (Array.isArray(value) ? value : [value]).filter((v) => v != null && v !== "").map(String);
    const [code] = Object.keys(errors);
    const messages = [...new Set(Object.values(errors).flat())];
    if (code) return new ApiRequestError(code, messages.join(" ") || "Request failed.", res.status, errors);
  }

  return new ApiRequestError("ERROR", `Request failed (${res.status}).`, res.status);
}

export interface RequestOptions {
  method?: "GET" | "POST" | "DELETE";
  body?: unknown;
  /** Internal: prevents the 401 → refresh → retry loop from recursing. */
  _retried?: boolean;
  /** A file sent as the body itself rather than as JSON: an adapter package. */
  file?: Blob;
  /** Lets a caller cancel the request - only `logout` needs this, and says why. */
  signal?: AbortSignal;
  /**
   * Work that can take long - prod's list to bring from, a bulk retry, an export - carried on in the
   * background if it outlasts a few seconds, and shown in the header under this name meanwhile.
   */
  background?: string;
  /** With `background`: how long the server waits before carrying it on, in seconds; three unless said. */
  wait?: number;
  /**
   * With `background`: once it has finished, answers `{ id }` rather than collecting the answer - for
   * a file the browser then downloads from a link, rather than holding it in memory.
   */
  leaveAnswer?: boolean;
  /** With `background`: told its id once it carries on there. */
  onBackground?: (id: string) => void;
}

/**
 * The one fetch helper every wired method goes through: prefixes the base,
 * attaches `Authorization: Bearer <jwt>`, includes credentials so the refresh
 * cookie rides along, and on 401 attempts a single silent refresh + retry.
 */
export async function request<T>(path: string, opts: RequestOptions = {}): Promise<T> {
  const res = await send(path, opts);

  if (res.status === 204) return undefined as T;
  const text = await res.text();
  if (!text) return undefined as T;
  // Some endpoints return a bare string as text/plain (e.g. /partners/generatekey),
  // which isn't valid JSON - only parse when the response actually is JSON.
  const isJson = res.headers.get("content-type")?.includes("application/json") ?? false;
  return (isJson ? JSON.parse(text) : text) as T;
}

/**
 * Sends a request and returns the successful response; see `request`.
 *
 * Work asked to carry on in the background (`background`, see `./background`) asks the server to
 * answer within a few seconds or say it carried on, and the answer it keeps is what this then
 * returns, as if the work had simply been slow. The page waiting for it never knows the difference.
 */
async function send(path: string, opts: RequestOptions): Promise<Response> {
  const token = getToken();
  const method = opts.method ?? "GET";
  const carryOn = Boolean(opts.background);

  // "Open" in the header asked for this read's kept answer, rather than asking all over again.
  const kept = carryOn && method === "GET" ? takeShown(path) : undefined;
  if (kept) {
    try {
      return await send(`/background/${kept.id}/result`, {});
    } catch {
      /* no longer kept: asked again below */
    }
  }

  const page = carryOn ? currentPage() : null;
  const label = opts.background ?? null;
  // Backend command handlers (POST) bind a JSON body, so they always need
  // `Content-Type: application/json` - even a body-less command like logout.
  // Without it the framework rejects the call with 415 before the handler runs;
  // an empty `{}` satisfies it.
  const sendJson = method === "POST" && !opts.file;
  const res = await fetch(`${API_BASE}${path}`, {
    method,
    credentials: "include",
    headers: {
      ...(sendJson ? { "Content-Type": "application/json" } : {}),
      ...(opts.file ? { "Content-Type": opts.file.type || "application/zip" } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(carryOn
        ? {
            Prefer: `respond-async, wait=${opts.wait ?? WAIT_SECONDS}`,
            "X-Bitween-Page": encodeURIComponent(page!),
            "X-Bitween-Label": encodeURIComponent(label!),
          }
        : {}),
    },
    body: opts.file ?? (sendJson ? JSON.stringify(opts.body ?? {}) : undefined),
    ...(opts.signal ? { signal: opts.signal } : {}),
  });

  if (res.status === 202 && carryOn) {
    const started = (await res.json()) as { id: string; status: BackgroundStatus };
    opts.onBackground?.(started.id);
    await carriedOn({ id: started.id, status: started.status, path: method === "GET" ? path : `${method} ${path}`, page, label });
    if (opts.leaveAnswer)
      return new Response(JSON.stringify({ id: started.id }), { headers: { "Content-Type": "application/json" } });
    return await send(`/background/${started.id}/result`, {});
  }

  if (res.status === 401 && !opts._retried) {
    const refreshed = await silentRefresh();
    if (refreshed) return send(path, { ...opts, _retried: true });
    clearToken();
    // Tell the app, not only the caller. Every page with a read in flight is about
    // to render its own small error, and a page's error state cannot end a session.
    sessionEndedListener?.();
    throw new ApiRequestError("UNAUTHENTICATED", "Your session has ended. Please sign in again.");
  }

  if (!res.ok) throw await toApiError(res);
  return res;
}

export const get = <T>(path: string): Promise<T> => request<T>(path);
export const post = <T>(path: string, body?: unknown): Promise<T> =>
  request<T>(path, { method: "POST", body });

/**
 * For a secondary read that only enriches a page - the "used by" counts, which come from another
 * area's list. Those are permission-guarded in their own right, so a role that can see this page
 * but not that area would otherwise take the whole page down with it. The enrichment is worth
 * losing; the page isn't. Only refusals are swallowed, so a real outage still surfaces.
 */
export function getEnrichment<T>(path: string, fallback: T): Promise<T> {
  return enrichment(get<T>(path), fallback);
}

/** `getEnrichment` for a read that is more than one GET - another area's whole list, say. */
export async function enrichment<T>(read: Promise<T>, fallback: T): Promise<T> {
  try {
    return await read;
  } catch (e) {
    // A refusal for *this* read only. UNAUTHENTICATED deliberately isn't swallowed: that one means
    // the session itself is gone, and the app needs to hear about it.
    const code = e instanceof ApiRequestError ? e.code : "";
    if (code === "HTTP_401" || code === "HTTP_403") return fallback;
    throw e;
  }
}
