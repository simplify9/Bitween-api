/** "Purchase Order" → "PURCHASE_ORDER" */
export const suggestCode = (name: string) =>
  name
    .trim()
    .replace(/[^a-zA-Z0-9]+/g, "_")
    .replace(/^_+|_+$/g, "")
    .replace(/^(\d)/, "T$1")
    .toUpperCase()
    .slice(0, 50);

/** "SAP Production" → "sap-production" */
export const suggestSlug = (name: string) =>
  name
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 50);

/**
 * What a person is typing into a URL-name box, kept usable as a path segment.
 *
 * Spaces become hyphens as you type rather than being rejected on save: the box
 * looks like a name field, so people type "returns intake", and the gateway that
 * saves is one whose endpoint 404s with nothing on screen saying why.
 *
 * A trailing separator survives, or "orders-" could never become "orders-inbound".
 * `finishUrlName` takes it off at save time, which is when it has to be gone.
 */
export const toUrlName = (typed: string) =>
  typed
    .toLowerCase()
    .replace(/[^a-z0-9_-]+/g, "-")
    .replace(/^[-_]+/, "")
    .slice(0, 50);

/** `toUrlName` plus the trailing separator that only mattered mid-typing. */
export const finishUrlName = (typed: string) => toUrlName(typed).replace(/[-_]+$/, "");
