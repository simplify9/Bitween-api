import { createContext, useContext, useEffect } from "react";
import { useLocation } from "react-router";

/** A page's own name, and the address it was given for, so a late answer can't label the next page. */
export interface NamedPage {
  path: string;
  title: string;
}

export const PageTitleContext = createContext<(named: NamedPage | null) => void>(() => {});

/**
 * What the page on screen is about — "Orders inbound" rather than "API gateway" — for its
 * breadcrumb and the browser tab. Pass nothing while it is loading; the page's generic title
 * stands in until then.
 */
export function usePageTitle(title: string | null | undefined) {
  const name = useContext(PageTitleContext);
  const { pathname } = useLocation();
  useEffect(() => {
    if (!title) return;
    name({ path: pathname, title });
    return () => name(null);
  }, [title, pathname, name]);
}
