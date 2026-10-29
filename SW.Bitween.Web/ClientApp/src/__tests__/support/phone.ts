import { afterEach, beforeEach } from "vitest";

/**
 * Puts the tests of the calling `describe` on a phone: every width query matches, so `usePhone`
 * and `useTooNarrowForEditor` both answer yes. jsdom has no `matchMedia` at all, which is why
 * every other test sees the desktop layout.
 */
export function onAPhone() {
  let had: PropertyDescriptor | undefined;
  beforeEach(() => {
    had = Object.getOwnPropertyDescriptor(window, "matchMedia");
    Object.defineProperty(window, "matchMedia", {
      configurable: true,
      writable: true,
      value: (query: string) => ({
        matches: /max-width/.test(query),
        media: query,
        onchange: null,
        addEventListener() {},
        removeEventListener() {},
        addListener() {},
        removeListener() {},
        dispatchEvent: () => false,
      }),
    });
  });
  afterEach(() => {
    if (had) Object.defineProperty(window, "matchMedia", had);
    else delete (window as { matchMedia?: unknown }).matchMedia;
  });
}
