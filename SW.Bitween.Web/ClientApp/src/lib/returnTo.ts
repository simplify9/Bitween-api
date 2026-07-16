import { useLocation, useSearchParams } from "react-router";

/**
 * The "detour" protocol: flows that need another entity (a wizard needing a
 * new partner, a create page needing a new information type) ROUTE to that
 * entity's own create/edit page, carrying where to come back to. The entity
 * page shows a "Continue …" banner; continuing returns — with the created
 * entity's id in a `picked` param so the origin can auto-select it.
 * Everything stays URL-based; origin drafts survive in sessionStorage.
 */
export interface ReturnContext {
  /** URL to go back to (path + search, `picked` stripped). */
  to: string;
  /** What the user was doing, e.g. "Attaching a partner to Orders inbound". */
  label: string;
}

/** Append a return context to a target path. */
export const withReturn = (path: string, ctx: ReturnContext): string => {
  const sep = path.includes("?") ? "&" : "?";
  return `${path}${sep}return=${encodeURIComponent(ctx.to)}&returnLabel=${encodeURIComponent(ctx.label)}`;
};

/** Read the inbound return context from the current URL, if any. */
export const useReturnContext = (): ReturnContext | null => {
  const [params] = useSearchParams();
  const to = params.get("return");
  if (!to) return null;
  return { to, label: params.get("returnLabel") ?? "where you were" };
};

/** This page's own URL, usable as a return target (drops any picked param). */
export const useHereAsReturnTarget = (): string => {
  const { pathname, search } = useLocation();
  const params = new URLSearchParams(search);
  params.delete("picked");
  const query = params.toString();
  return query ? `${pathname}?${query}` : pathname;
};

/** Where "Continue" goes: back to the origin, optionally reporting a pick. */
export const continueUrl = (ctx: ReturnContext, picked?: string): string => {
  if (!picked) return ctx.to;
  const sep = ctx.to.includes("?") ? "&" : "?";
  return `${ctx.to}${sep}picked=${encodeURIComponent(picked)}`;
};

/**
 * Read-and-consume a `picked=kind:id` param on the origin page. Returns the
 * id when the kind matches; the caller applies it to its draft.
 */
export const takePicked = (
  params: URLSearchParams,
  kind: string,
): number | null => {
  const raw = params.get("picked");
  if (!raw) return null;
  const [k, id] = raw.split(":");
  return k === kind && id ? Number(id) : null;
};
