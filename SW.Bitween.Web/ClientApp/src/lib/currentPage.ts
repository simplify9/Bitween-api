import { useMatches } from "react-router";
import type { PageId } from "../pages";

/** The registry page the router matched (see src/router.tsx), or null on one outside it, like a 404. */
export function useCurrentPage(): PageId | null {
  const matches = useMatches();
  for (let i = matches.length - 1; i >= 0; i--) {
    const handle = matches[i].handle as { page?: PageId } | undefined;
    if (handle?.page) return handle.page;
  }
  return null;
}
