import { useEffect, useState } from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router";

/**
 * Leaving a page to create or open something it points at - a response subscription, a
 * notification channel - and coming back to it.
 *
 * The page being left usually holds a draft nothing has saved: a subscription that doesn't
 * exist yet, or unsaved edits to one that does. It is kept in sessionStorage under the page's
 * path, and the page's own URL is marked so the way back - Create, Cancel, or the browser's
 * Back - restores it. A draft left behind by a trip that never came back is never restored,
 * because nothing arrives at the page carrying the mark.
 *
 * What was created comes back as `?pickedResponse=` or `?pickedChannel=`, and the `slot` kept
 * with the draft says which picker asked for it, when a page has several.
 */

const STORE = "bitween-detour:";
const KEPT = "draftKept";
const PICKED = { response: "pickedResponse", channel: "pickedChannel" } as const;
const FEEDER = "feeder";

export type Picked = keyof typeof PICKED;

export const NEW_RESPONSE_PATH = "/response-subscriptions/new";
export const NEW_CHANNEL_PATH = "/settings/notification-channels/new";

/**
 * The create page's address: the way back, and the subscription it is being made for when that one
 * is saved, so the page can offer what that subscription receives as source values.
 */
export function newResponsePath(back: string, feederId: number | null = null): string {
  return `${NEW_RESPONSE_PATH}?return=${encodeURIComponent(back)}${feederId !== null ? `&${FEEDER}=${feederId}` : ""}`;
}

/** A channel's page - or the create page, for null - with the way back. */
export function channelPath(back: string, channelId: number | null = null): string {
  return `${channelId === null ? NEW_CHANNEL_PATH : `/settings/notification-channels/${channelId}`}?return=${encodeURIComponent(back)}`;
}

/** The subscription a new response subscription is being made for, when the create page was told. */
export function feederOf(params: URLSearchParams): number | null {
  const id = Number(params.get(FEEDER));
  return Number.isInteger(id) && id > 0 ? id : null;
}

/** Where a create page sends you once the thing exists (or null on Cancel). */
export function returnPath(returnTo: string, picked: number | null, what: Picked = "response"): string {
  if (picked === null) return returnTo;
  return `${returnTo}${returnTo.includes("?") ? "&" : "?"}${PICKED[what]}=${picked}`;
}

/**
 * Only a path inside the app counts - `?return=` is a link anyone could hand you. Browsers read
 * a backslash as a slash, so `/\evil.example` leaves the app just as `//evil.example` does.
 */
export function safeReturn(value: string | null): string | null {
  return value && value.startsWith("/") && !value.startsWith("//") && !value.includes("\\") ? value : null;
}

interface Stash<T> {
  draft: T;
  slot: string | null;
}

export function useDetour<T>() {
  const location = useLocation();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();

  // Read once: the page seeds its draft from this on first render.
  const [arrival] = useState(() => {
    const stash = params.get(KEPT) === "1" ? read<T>(location.pathname) : null;
    const picked = (what: Picked) => (params.get(PICKED[what]) ? Number(params.get(PICKED[what])) : null);
    return {
      kept: stash?.draft ?? null,
      slot: stash?.slot ?? null,
      pickedResponse: picked("response"),
      pickedChannel: picked("channel"),
    };
  });

  // Then the marks come off, so a refresh opens the page fresh rather than restoring again.
  useEffect(() => {
    if (!params.has(KEPT) && !params.has(PICKED.response) && !params.has(PICKED.channel)) return;
    forget(location.pathname);
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        next.delete(KEPT);
        next.delete(PICKED.response);
        next.delete(PICKED.channel);
        return next;
      },
      { replace: true },
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  /** Keeps `draft`, then goes to wherever `to` says given the way back here. */
  const away = (draft: T, to: (back: string) => string, slot: string | null = null) => {
    try {
      sessionStorage.setItem(STORE + location.pathname, JSON.stringify({ draft, slot } satisfies Stash<T>));
    } catch {
      // Storage refused (private mode, full): the page comes back as it would on a refresh.
    }
    const here = new URLSearchParams(location.search);
    here.set(KEPT, "1");
    here.delete(PICKED.response);
    here.delete(PICKED.channel);
    const back = `${location.pathname}?${here}`;
    // Marked in place first, so the browser's Back lands on the marked entry too.
    navigate(back, { replace: true });
    navigate(to(back));
  };

  return {
    kept: arrival.kept,
    /** Which picker sent you away, as the page named it when it left. */
    slot: arrival.slot,
    pickedResponse: arrival.pickedResponse,
    pickedChannel: arrival.pickedChannel,
    away,
    /**
     * Keeps `draft` and opens the response subscription's create page. `feederId` is the
     * subscription being left, when it is saved: a new one has never run, so has nothing to offer.
     */
    leave: (draft: T, feederId: number | null = null) => away(draft, (back) => newResponsePath(back, feederId)),
    /** Keeps `draft` and opens an existing subscription; Back comes home to the draft. */
    open: (draft: T, subscriptionId: number) => away(draft, () => `/subscriptions/${subscriptionId}`),
    /** Keeps `draft` and opens the new channel page; Create comes back with `pickedChannel`. */
    newChannel: (draft: T, slot: string) => away(draft, (back) => channelPath(back), slot),
    /** Keeps `draft` and opens a channel's page; Save or Back comes home to the draft. */
    openChannel: (draft: T, channelId: number, slot: string = "channel") =>
      away(draft, (back) => channelPath(back, channelId), slot),
  };
}

function read<T>(pathname: string): Stash<T> | null {
  try {
    const raw = sessionStorage.getItem(STORE + pathname);
    return raw === null ? null : (JSON.parse(raw) as Stash<T>);
  } catch {
    return null;
  }
}

function forget(pathname: string) {
  try {
    sessionStorage.removeItem(STORE + pathname);
  } catch {
    // Nothing to clean up if storage is unavailable.
  }
}
