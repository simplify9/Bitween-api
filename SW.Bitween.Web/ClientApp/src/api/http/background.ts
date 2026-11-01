import type { HubConnection } from "@microsoft/signalr";
import { API_BASE, getToken, request } from "./request";

/**
 * Background requests, the admin UI's side.
 *
 * Work that can take long - prod's list to bring from, bringing, a bulk retry, an export - is sent
 * asking to carry on in the background (`request`'s `background`). If the server is still at it after
 * a few seconds it answers 202 with an id and carries on there (BackgroundRequestMiddleware). The call
 * here keeps waiting - its page still gets the answer when it comes - while the header shows it, so
 * the person can go and do something else. The server says when one changes, and how far it has got,
 * over SignalR; this keeps the list, and collects the answer for the call that wanted it.
 */

export type BackgroundStatus = "Waiting" | "Running" | "Done" | "Cancelled" | "TimedOut" | "Stopped";

export interface BackgroundRequest {
  id: string;
  /** What the work is called, as the call that started it named it. */
  label: string | null;
  /** The UI address it was asked from, which is where its answer is shown. */
  page: string | null;
  /** The API path asked, without the /api prefix; a write's begins with its method. */
  path: string;
  status: BackgroundStatus;
  startedOn: string;
  finishedOn: string | null;
  /** How far it has got, for work that counts its steps. */
  done?: number | null;
  total?: number | null;
  summary?: string | null;
}

export interface BackgroundState {
  requests: BackgroundRequest[];
}

/** How long the server is asked to wait before carrying work on in the background. */
export const WAIT_SECONDS = 3;

/** How often to ask for the list while SignalR can't be reached and something is running. */
const POLL_MS = 3_000;

/** How long the connection stays open with nothing running, before it's closed. */
const IDLE_MS = 60_000;

const DISMISSED_KEY = "bitween_background_dismissed";

export const isRunning = (status: BackgroundStatus): boolean => status === "Waiting" || status === "Running";

let state: BackgroundState = { requests: [] };
const listeners = new Set<() => void>();
/** Calls waiting for a request to finish, by its id. */
const waiters = new Map<string, Array<() => void>>();

const dismissed = new Set<string>(readDismissed());

function readDismissed(): string[] {
  try {
    return JSON.parse(localStorage.getItem(DISMISSED_KEY) ?? "[]") as string[];
  } catch {
    return [];
  }
}

function saveDismissed(): void {
  try {
    // Ids only last as long as the server keeps their answers, so the newest hundred is plenty.
    localStorage.setItem(DISMISSED_KEY, JSON.stringify([...dismissed].slice(-100)));
  } catch {
    /* storage can be unavailable; a dismissed row then comes back after a reload, nothing worse */
  }
}

function publish(next: BackgroundState): void {
  state = next;
  for (const listener of listeners) listener();
  for (const r of state.requests) if (!isRunning(r.status)) wake(r.id);
  if (state.requests.some((r) => isRunning(r.status))) listen();
  else idle();
}

function wake(id: string): void {
  const waiting = waiters.get(id);
  if (!waiting) return;
  waiters.delete(id);
  for (const resolve of waiting) resolve();
}

export const subscribeBackground = (listener: () => void): (() => void) => {
  listeners.add(listener);
  return () => listeners.delete(listener);
};

export const backgroundState = (): BackgroundState => state;

/**
 * `changed` over what was `known` of it - unless it says running about work already over, which is
 * only news that came late: a progress push after the answer, a list read before it.
 */
function merged(known: BackgroundRequest | undefined, changed: Partial<BackgroundRequest>): BackgroundRequest {
  if (known && !isRunning(known.status) && changed.status && isRunning(changed.status)) return known;
  return { ...known, ...changed } as BackgroundRequest;
}

function upsert(changed: Partial<BackgroundRequest> & { id: string }): boolean {
  const known = state.requests.find((r) => r.id === changed.id);
  if (!known && !changed.path) return false;
  const next = merged(known, changed);
  if (!isRunning(next.status) && !next.finishedOn) next.finishedOn = new Date().toISOString();
  publish({ requests: [next, ...state.requests.filter((r) => r.id !== changed.id)] });
  return true;
}

let page: string | null = null;

/**
 * Where the person is, as the router says it; the shell tells, before any page it shows asks for
 * anything, so work is known by the page that asked for it.
 */
export function setCurrentPage(at: string): void {
  page = at;
}

/** The UI address of the page asking. */
export function currentPage(): string {
  return page ?? window.location.pathname + window.location.search;
}

/** Answers "Open" asked to show, by read: the request whose kept answer the read is given. */
const toShow = new Map<string, BackgroundRequest>();

/** The request whose kept answer a read should be given instead of asking again, once; see `openBackground`. */
export function takeShown(path: string): BackgroundRequest | undefined {
  const r = toShow.get(path);
  toShow.delete(path);
  return r;
}

/** A call's work carried on in the background: shown in the header until it finishes, which this waits for. */
export async function carriedOn(request: Omit<BackgroundRequest, "startedOn" | "finishedOn">): Promise<void> {
  if (!state.requests.some((r) => r.id === request.id))
    upsert({ ...request, startedOn: new Date().toISOString(), finishedOn: null });
  const known = state.requests.find((r) => r.id === request.id);
  if (known && !isRunning(known.status)) return;
  await new Promise<void>((resolve) => waiters.set(request.id, [...(waiters.get(request.id) ?? []), resolve]));
}

let refreshing: Promise<void> | null = null;

/** Which session a list asked for belongs to: one asked before signing out mustn't fill the next. */
let session = 0;

/**
 * Brings the list up to date from the server: on loading the app, on reconnecting, and while the
 * server can only be asked.
 */
export function refreshBackground(): Promise<void> {
  const asked = session;
  refreshing ??= request<BackgroundRequest[]>("/background")
    .then((rows) => {
      if (asked !== session) return;
      const known = new Map(state.requests.map((r) => [r.id, r]));
      for (const row of rows ?? []) {
        if (dismissed.has(row.id) && !isRunning(row.status)) continue;
        const path = row.path.replace(/^(\w+ )?\/api(?=\/)/, "$1");
        known.set(row.id, merged(known.get(row.id), { ...row, path }));
      }
      publish({
        requests: [...known.values()]
          .filter((r) => !dismissed.has(r.id) || isRunning(r.status))
          .sort((a, b) => b.startedOn.localeCompare(a.startedOn)),
      });
    })
    .catch(() => {
      /* signed out, or the server is older than background requests: nothing to show */
    })
    .finally(() => {
      if (asked === session) refreshing = null;
    });
  return refreshing;
}

/** Stops them, wherever they run. Work that counts its steps ends at its next one, and says how far it got. */
export async function cancelBackground(ids: string[]): Promise<void> {
  await Promise.all(ids.map((id) => request(`/background/${id}`, { method: "DELETE" }).catch(() => undefined)));
  // The server says when they've ended; until then the list says it was asked.
  void refreshBackground();
}

/** Takes them off the list: their answers have been seen, or aren't wanted. */
export function dismissBackground(ids: string[]): void {
  for (const id of ids) dismissed.add(id);
  saveDismissed();
  publish({ requests: state.requests.filter((r) => !ids.includes(r.id) || isRunning(r.status)) });
}

/**
 * Going back to what they were for: the page they were asked from is told to show their kept
 * answers rather than ask again, and they leave the list.
 */
export function openBackground(requests: BackgroundRequest[]): void {
  for (const r of requests) if (r.status === "Done" && !/^\w+ /.test(r.path)) toShow.set(r.path, r);
  dismissBackground(requests.filter((r) => !isRunning(r.status)).map((r) => r.id));
}

/** The session ended: nothing of it may show to whoever signs in next in this tab. */
export function resetBackground(): void {
  session++;
  refreshing = null;
  const connection = hub;
  hub = null;
  void connection?.stop();
  if (polling) clearInterval(polling);
  if (idling) clearTimeout(idling);
  polling = null;
  idling = null;
  toShow.clear();
  // The calls still waiting are told they're over; their answers are refused, as the session is.
  const waiting = [...waiters.keys()];
  state = { requests: [] };
  for (const id of waiting) wake(id);
  for (const listener of listeners) listener();
}

let hub: HubConnection | null = null;
let connecting = false;
let polling: ReturnType<typeof setInterval> | null = null;
let idling: ReturnType<typeof setTimeout> | null = null;

interface Change {
  id: string;
  status: BackgroundStatus;
  done?: number | null;
  total?: number | null;
  summary?: string | null;
}

function onChanged(change: Change): void {
  // One another tab started, or this tab before a reload: the list says what it is.
  if (!upsert(change)) void refreshBackground();
}

/**
 * Opens the connection the server tells changes over, or asks it every few seconds if it can't. The
 * SignalR client is loaded only then: most visits never carry anything on, and needn't download it.
 */
function listen(): void {
  if (idling) {
    clearTimeout(idling);
    idling = null;
  }
  if (hub || polling || connecting) return;
  connecting = true;
  import("@microsoft/signalr")
    .then(({ HubConnectionBuilder, HttpTransportType, LogLevel }) => {
      connecting = false;
      if (hub || polling || !state.requests.some((r) => isRunning(r.status))) return;
      // WebSockets only: no negotiation, so no request has to land on the same node as the last
      // one, and a deployment needs no sticky sessions. Where they're blocked, the list is asked.
      const connection = new HubConnectionBuilder()
        .withUrl(`${API_BASE}/background/hub`, {
          accessTokenFactory: () => getToken() ?? "",
          skipNegotiation: true,
          transport: HttpTransportType.WebSockets,
        })
        .withAutomaticReconnect()
        .configureLogging(LogLevel.None)
        .build();
      connection.on("changed", onChanged);
      // Anything that changed while it was away is told no more; the list says.
      connection.onreconnected(() => void refreshBackground());
      connection.onclose(() => {
        if (hub !== connection) return;
        hub = null;
        if (state.requests.some((r) => isRunning(r.status))) poll();
      });
      hub = connection;
      return connection
        .start()
        .then(() => refreshBackground())
        .catch(() => {
          if (hub !== connection) return;
          hub = null;
          poll();
        });
    })
    .catch(() => {
      connecting = false;
      poll();
    });
}

function poll(): void {
  if (polling) return;
  polling = setInterval(() => {
    if (!state.requests.some((r) => isRunning(r.status))) {
      clearInterval(polling!);
      polling = null;
      return;
    }
    void refreshBackground();
  }, POLL_MS);
}

function idle(): void {
  if (idling || (!hub && !polling)) return;
  idling = setTimeout(() => {
    idling = null;
    if (polling) {
      clearInterval(polling);
      polling = null;
    }
    const connection = hub;
    hub = null;
    void connection?.stop();
  }, IDLE_MS);
}
