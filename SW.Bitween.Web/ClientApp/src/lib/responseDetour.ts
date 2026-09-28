import { useEffect, useState } from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router";

/**
 * Leaving a page for a response subscription — to create one, or to open the one it hands
 * its response to — and coming back to it.
 *
 * The page being left usually holds a draft nothing has saved: a subscription that doesn't
 * exist yet, or unsaved edits to one that does. It is kept in sessionStorage under the page's
 * path, and the page's own URL is marked so the way back — Create, Cancel, or the browser's
 * Back — restores it. A draft left behind by a trip that never came back is never restored,
 * because nothing arrives at the page carrying the mark.
 *
 * The new response subscription comes back as `?pickedResponse=`.
 */

const STORE = "bitween-response-detour:";
const KEPT = "draftKept";
const PICKED = "pickedResponse";

export const NEW_RESPONSE_PATH = "/response-subscriptions/new";

/** Where the create page sends you once the response subscription exists (or null on Cancel). */
export function returnPath(returnTo: string, picked: number | null): string {
  if (picked === null) return returnTo;
  return `${returnTo}${returnTo.includes("?") ? "&" : "?"}${PICKED}=${picked}`;
}

/**
 * Only a path inside the app counts — `?return=` is a link anyone could hand you. Browsers read
 * a backslash as a slash, so `/\evil.example` leaves the app just as `//evil.example` does.
 */
export function safeReturn(value: string | null): string | null {
  return value && value.startsWith("/") && !value.startsWith("//") && !value.includes("\\") ? value : null;
}

export function useResponseDetour<T>() {
  const location = useLocation();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();

  // Read once: the page seeds its draft from this on first render.
  const [arrival] = useState(() => ({
    kept: params.get(KEPT) === "1" ? read<T>(location.pathname) : null,
    pickedResponse: params.get(PICKED) ? Number(params.get(PICKED)) : null,
  }));

  // Then the marks come off, so a refresh opens the page fresh rather than restoring again.
  useEffect(() => {
    if (!params.has(KEPT) && !params.has(PICKED)) return;
    forget(location.pathname);
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        next.delete(KEPT);
        next.delete(PICKED);
        return next;
      },
      { replace: true },
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  /** Keeps `draft`, then goes to wherever `to` says given the way back here. */
  const away = (draft: T, to: (back: string) => string) => {
    try {
      sessionStorage.setItem(STORE + location.pathname, JSON.stringify(draft));
    } catch {
      // Storage refused (private mode, full): the page comes back as it would on a refresh.
    }
    const here = new URLSearchParams(location.search);
    here.set(KEPT, "1");
    here.delete(PICKED);
    const back = `${location.pathname}?${here}`;
    // Marked in place first, so the browser's Back lands on the marked entry too.
    navigate(back, { replace: true });
    navigate(to(back));
  };

  return {
    kept: arrival.kept,
    pickedResponse: arrival.pickedResponse,
    /** Keeps `draft` and opens the response subscription's create page. */
    leave: (draft: T) => away(draft, (back) => `${NEW_RESPONSE_PATH}?return=${encodeURIComponent(back)}`),
    /** Keeps `draft` and opens an existing subscription; Back comes home to the draft. */
    open: (draft: T, subscriptionId: number) => away(draft, () => `/subscriptions/${subscriptionId}`),
  };
}

function read<T>(pathname: string): T | null {
  try {
    const raw = sessionStorage.getItem(STORE + pathname);
    return raw === null ? null : (JSON.parse(raw) as T);
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
