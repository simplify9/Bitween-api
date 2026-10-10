import { ChevronRight, Search } from "lucide-react";

const isMac = typeof navigator !== "undefined" && /Mac|iPhone|iPad/.test(navigator.platform);
import { Link, useMatches } from "react-router";
import { NAV_GROUPS } from "../../nav";
import { PAGES, pageTrail, pathOf, type PageId } from "../../pages";
import type { Branding } from "../../lib/branding";

/**
 * Where you are, and which instance you are on.
 *
 * The trail comes from the page registry: each page names its parent, so a page three levels
 * down (an API gateway → attaching a partner → a new subscription for it) shows the way back to
 * each level rather than one "Back". The last crumb is the thing itself when the page has said
 * what it is (`usePageTitle`).
 */
export function TopBar({
  page,
  title,
  branding,
  onSearch,
}: {
  page: PageId | null;
  title: string | null;
  branding: Branding;
  onSearch: () => void;
}) {
  return (
    <div className="sticky top-0 z-20 hidden h-12 shrink-0 items-center gap-4 border-b border-ink-100 bg-canvas/90 px-6 backdrop-blur lg:flex">
      <div className="min-w-0 flex-1">{page && <Breadcrumbs page={page} title={title} />}</div>
      <button
        onClick={onSearch}
        className="flex h-8 w-64 shrink-0 items-center gap-2 rounded-lg border border-ink-200 bg-white px-2.5 text-[13px] text-ink-500 hover:border-ink-300"
      >
        <Search className="size-3.5" aria-hidden />
        <span className="flex-1 text-left">Search</span>
        <kbd className="rounded border border-ink-200 px-1 font-mono text-[11px]">{isMac ? "⌘K" : "Ctrl K"}</kbd>
      </button>
      <EnvironmentBadge branding={branding} />
    </div>
  );
}

function Breadcrumbs({ page, title }: { page: PageId; title: string | null }) {
  const matches = useMatches();
  const params = matches[matches.length - 1]?.params ?? {};
  const trail = pageTrail(page);
  const group = NAV_GROUPS.find((g) => g.items.some((i) => i.id === trail[0]))?.label;

  return (
    <nav aria-label="Breadcrumb">
      <ol className="flex min-w-0 items-center gap-1.5 text-[13px] text-ink-500">
        {group && <li className="shrink-0">{group}</li>}
        {trail.map((id, i) => {
          const last = i === trail.length - 1;
          const label = last ? (title ?? PAGES[id].title) : PAGES[id].title;
          const href = pathOf(id, params);
          return (
            <li key={id} className={`flex min-w-0 items-center gap-1.5 ${last ? "" : "shrink-0"}`}>
              {(i > 0 || group) && <ChevronRight className="size-3.5 shrink-0 text-ink-300" aria-hidden />}
              {last || !href ? (
                <span
                  aria-current={last ? "page" : undefined}
                  className={`truncate ${last ? "font-medium text-ink-800" : ""}`}
                >
                  {label}
                </span>
              ) : (
                <Link to={href} className="hover:text-ink-800 hover:underline">
                  {label}
                </Link>
              )}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}

/** Set under Settings → Brand & theme. Nothing at all until someone names the environment. */
export function EnvironmentBadge({ branding }: { branding: Branding }) {
  const name = branding.environmentName?.trim();
  if (!name) return null;
  return (
    <span
      className="inline-flex shrink-0 items-center gap-1.5 rounded-full border bg-white px-2.5 py-0.5 text-[12px] font-semibold text-ink-800"
      style={{ borderColor: branding.environmentColor }}
      title="The environment this instance is"
    >
      <span className="size-2 rounded-full" style={{ backgroundColor: branding.environmentColor }} aria-hidden />
      {name}
    </span>
  );
}
