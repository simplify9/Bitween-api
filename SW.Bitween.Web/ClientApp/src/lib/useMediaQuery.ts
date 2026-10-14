import { useEffect, useState } from "react";

/**
 * Whether a media query matches, kept current as the window changes. Off where it can't be
 * asked, as in tests: jsdom has no `matchMedia`, so every test sees the desktop layout, which is
 * what the existing tests were written against.
 *
 * Rendering one layout or the other, rather than both and hiding one with CSS, is deliberate:
 * two copies of a list put every name and button in the page twice, for screen readers and for
 * anything that finds a row by its text.
 */
export function useMediaQuery(query: string): boolean {
  const [matches, setMatches] = useState(() => typeof window.matchMedia === "function" && window.matchMedia(query).matches);
  useEffect(() => {
    if (typeof window.matchMedia !== "function") return;
    const mq = window.matchMedia(query);
    const on = () => setMatches(mq.matches);
    on();
    mq.addEventListener?.("change", on);
    return () => mq.removeEventListener?.("change", on);
  }, [query]);
  return matches;
}

/** Below Tailwind's `sm` breakpoint: a phone held upright, where tables turn into cards. */
export const PHONE_QUERY = "(max-width: 639px)";

export const usePhone = () => useMediaQuery(PHONE_QUERY);

/**
 * Narrower than the width a canvas or a heavy editor needs to be usable. Below it those screens
 * show a summary card with the everyday actions instead, and say the editor opens on a wider
 * screen.
 */
export const EDITOR_MIN_WIDTH = 900;

export const useTooNarrowForEditor = () => useMediaQuery(`(max-width: ${EDITOR_MIN_WIDTH - 1}px)`);
