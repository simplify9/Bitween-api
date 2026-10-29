/**
 * The empty list a query's rows fall back to before they arrive.
 *
 * `query.data ?? []` is a new array on every render, so a useMemo over it recomputes on every
 * render too. This one is always the same array; `never[]` keeps anything from being added to it.
 */
export const NONE: never[] = [];
