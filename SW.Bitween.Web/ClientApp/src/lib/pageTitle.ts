import { createContext, useContext, useEffect } from "react";
import { useLocation, useParams } from "react-router";
import { pathOf, type PageId } from "../pages";

/** A page's own name, and the address it was given for, so a late answer can't label the next page. */
export interface NamedPage {
  path: string;
  title: string;
}

export const PageTitleContext = createContext<(named: NamedPage) => void>(() => {});

/**
 * What the page on screen is about - "Orders inbound" rather than "API gateway" - for its
 * breadcrumb and the browser tab. Pass nothing while it is loading; the page's generic title
 * stands in until then.
 *
 * Names are kept by address after the page is left, so going one level down (the gateway's
 * "Attach a partner") still shows "Orders inbound" for the crumb that leads back up.
 */
export function usePageTitle(title: string | null | undefined) {
  const name = useContext(PageTitleContext);
  const { pathname } = useLocation();
  useEffect(() => {
    if (title) name({ path: pathname, title });
  }, [title, pathname, name]);
}

/**
 * Names a page further up the breadcrumb trail, for a page opened straight from a link that has
 * loaded its parent anyway: the gateway's "Attach a partner" knows the gateway's name, so the
 * crumb before it can say so even when the gateway's own page was never open.
 */
export function useCrumbTitle(page: PageId, title: string | null | undefined) {
  const name = useContext(PageTitleContext);
  const path = pathOf(page, useParams());
  useEffect(() => {
    if (title && path) name({ path, title });
  }, [title, path, name]);
}
