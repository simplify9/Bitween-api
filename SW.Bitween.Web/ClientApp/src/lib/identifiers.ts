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
