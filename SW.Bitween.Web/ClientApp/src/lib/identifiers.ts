/** "Café Münster" → "Cafe Munster": letters keep their base, rather than the accented ones being dropped. */
const unaccent = (text: string) => text.normalize("NFKD").replace(/[\u0300-\u036f]/g, "");

/** "Purchase Order" → "PURCHASE_ORDER" */
export const suggestCode = (name: string) =>
  unaccent(name)
    .trim()
    .replace(/[^a-zA-Z0-9]+/g, "_")
    .replace(/^_+|_+$/g, "")
    .replace(/^(\d)/, "T$1")
    .toUpperCase()
    .slice(0, 50);

/** "SAP Production" → "sap-production" */
export const suggestSlug = (name: string) =>
  unaccent(name)
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 50);

/**
 * What a person is typing into a URL-name box, kept usable as a path.
 *
 * Spaces become hyphens as you type rather than being rejected on save: the box
 * looks like a name field, so people type "returns intake", and the gateway that
 * saves is one whose endpoint 404s with nothing on screen saying why.
 *
 * `/` splits it into parts ("logistics/slim/orders"), for clients with a URL scheme
 * of their own.
 *
 * A trailing separator survives, or "orders-" could never become "orders-inbound".
 * `finishUrlName` takes it off at save time, which is when it has to be gone.
 */
export const toUrlName = (typed: string) =>
  unaccent(typed)
    .toLowerCase()
    .replace(/[^a-z0-9_/-]+/g, "-")
    .replace(/([-_])[-_]+/g, "$1")
    .replace(/[-_]*\/[-_]*/g, "/")
    .replace(/\/{2,}/g, "/")
    .replace(/^[-_/]+/, "")
    .slice(0, 200);

/** `toUrlName` minus the trailing separator that only mattered mid-typing. */
export const finishUrlName = (typed: string) => toUrlName(typed).replace(/[-_/]+$/, "");

/**
 * The API's url-name rules that typing can't be stopped from breaking, as the message
 * to show under the box — or null when it would save.
 *
 * "sync" can't be refused mid-typing, it may be on its way to "syncs"; so it's said
 * here instead, where the save button can wait on it.
 */
export const urlNameProblem = (typed: string): string | null => {
  const name = finishUrlName(typed);
  if (!name) return "A URL name is required.";
  const last = name.slice(name.lastIndexOf("/") + 1);
  if (last === "sync" || last === "async")
    return `It can't end in "${last}" — partners add /sync or /async after it.`;
  return null;
};
