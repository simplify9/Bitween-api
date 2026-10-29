import { useCallback } from "react";
import { useSearchParams } from "react-router";
import { useSearchText } from "./useSearchText";

/**
 * A list page's state, kept in its URL: the search, the filters, the page of results.
 *
 * Every list page used to write its own copy of this, and the copies had drifted: some replaced
 * the history entry on every filter change, so Back skipped straight past a filter you had just
 * set; some only while typing; and which changes went back to the first page differed. Here:
 *
 * - the search runs once typing pauses, and replaces the history entry (Back doesn't replay
 *   every keystroke);
 * - a filter or a page of results is a step Back undoes;
 * - changing anything but the page goes back to the first page.
 */
export function useListParams() {
  const [params, setParams] = useSearchParams();
  const q = params.get("q") ?? "";
  const offset = Number(params.get("offset")) || 0;

  const set = useCallback(
    (key: string, value: string | null) =>
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev);
          if (value) next.set(key, value);
          else next.delete(key);
          if (key !== "offset") next.delete("offset");
          return next;
        },
        { replace: key === "q" },
      ),
    [setParams],
  );

  const [searchText, setSearchText] = useSearchText(q, (text) => set("q", text || null));

  return {
    /** The URL's search params, for the page's own filters. */
    params,
    q,
    offset,
    /** Sets one parameter; null removes it. */
    set,
    /** The search box's text, which leads `q` by the pause in typing. */
    searchText,
    setSearchText,
  };
}
