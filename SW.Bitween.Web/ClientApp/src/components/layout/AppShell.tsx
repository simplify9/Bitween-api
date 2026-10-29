import { Suspense, useCallback, useEffect, useRef, useState } from "react";
import { Link, NavLink, Outlet, useLocation, useNavigate } from "react-router";
import { useQueryClient } from "@tanstack/react-query";
import {
  ArrowRight,
  ChevronDown,
  ChevronRight,
  LogOut,
  Menu as MenuIcon,
  PanelLeftClose,
  PanelLeftOpen,
  UserRound,
  X,
  Search as SearchIcon,
} from "lucide-react";
import type { SettingRow } from "../../api";
import { useSession } from "../../auth/useSession";
import { type Branding, useApplyBranding } from "../../lib/branding";
import { settingsDraft, useSettingsDraft } from "../../lib/settingsDraft";
import { visibleGroups } from "../../nav";
import { Avatar } from "../ui/Avatar";
import { Button } from "../ui/basics";
import { Menu, MenuItem } from "../ui/overlays";
import { keys } from "../../api/queryKeys";
import { LoadingBlock } from "../ui/basics";
import { PAGES } from "../../pages";
import { useCurrentPage } from "../../lib/currentPage";
import { PageTitleContext, type NamedPage } from "../../lib/pageTitle";
import { EnvironmentBadge, TopBar } from "./TopBar";
import { CommandPalette } from "./CommandPalette";
import { recordVisit } from "../../lib/recentPages";

/**
 * Shown on every page while unsaved setting changes exist - the whole app
 * renders with the draft applied, so this is the "you're previewing" strip
 * that routes back to Settings to keep editing or save.
 */
function SettingsPreviewBanner() {
  const draft = useSettingsDraft();
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const count = Object.keys(draft).length;
  if (count === 0 || pathname.startsWith("/settings")) return null;

  // Land back on the section holding the first pending change.
  const rows = queryClient.getQueryData<SettingRow[]>(keys.settings.list);
  const section = rows?.find((r) => r.key in draft)?.section;
  const editUrl = section ? `/settings?section=${encodeURIComponent(section)}` : "/settings";

  return (
    <div className="mb-4 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-crimson-200 bg-crimson-50/60 px-4 py-2.5">
      <p className="min-w-0 text-sm text-ink-700">
        Previewing <strong className="font-medium text-ink-900">{count} unsaved setting change{count === 1 ? "" : "s"}</strong>{" "}
        - this is how the app will look once saved.
      </p>
      <span className="flex shrink-0 gap-2">
        <Button size="sm" onClick={() => settingsDraft.discardAll()}>
          Discard
        </Button>
        <Button size="sm" variant="primary" onClick={() => navigate(editUrl)}>
          Continue editing <ArrowRight className="size-3.5" />
        </Button>
      </span>
    </div>
  );
}

function AppFooter({ branding }: { branding: Branding }) {
  const { footer, companyName } = branding;
  if (!branding.ready || !footer.show) return null;
  const links = [
    { label: "Website", href: footer.websiteUrl },
    { label: "LinkedIn", href: footer.linkedinUrl },
    { label: "GitHub", href: footer.githubUrl },
  ].filter((l) => l.href);

  return (
    <footer className="mt-auto border-t border-ink-100 px-4 py-3 sm:px-6">
      <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-ink-500">
        <span>
          {footer.copyrightIcon} {new Date().getFullYear()} {companyName}. {footer.copyrightText}
        </span>
        {links.length > 0 && (
          <span className="flex gap-4">
            {links.map((l) => (
              <a
                key={l.label}
                href={l.href}
                target="_blank"
                rel="noreferrer"
                // A line of 12px text is a small thing to hit with a thumb.
                className="touch-target relative font-medium text-ink-500 hover:link"
              >
                {l.label}
              </a>
            ))}
          </span>
        )}
      </div>
    </footer>
  );
}

const COLLAPSED_KEY = "bitween-nav-collapsed";

/**
 * Below this many pixels of window height even the tight list can't fit Administration as well,
 * so there it starts folded, as it used to everywhere.
 */
const SHORT_WINDOW = 640;

/**
 * Every group starts open. Administration used to start folded, because with it open the sidebar
 * ran past the bottom of a 900px-tall laptop screen; that meant an administrator's first visit
 * never showed Members or Settings. The rows are tighter now and the list says when it scrolls,
 * so it stays folded only on a window too short for it. Anyone without an Administration
 * permission never sees the group at all (`visibleGroups`). A group holding the current page is
 * always open, and once someone has folded or opened anything their choice is what's kept.
 */
const foldedAtFirst = (): string[] =>
  typeof window !== "undefined" && window.innerHeight < SHORT_WINDOW ? ["Administration"] : [];

const loadCollapsed = (): string[] => {
  try {
    const stored = localStorage.getItem(COLLAPSED_KEY);
    return stored === null ? foldedAtFirst() : (JSON.parse(stored) as string[]);
  } catch {
    return foldedAtFirst();
  }
};

/**
 * Whether a scrolling box has more above or below what shows. The account block sits right under
 * the nav, so a list cut off at the bottom looked like a list that ended: Roles, Settings, Nodes
 * and Audit trail were below the fold with nothing saying so.
 */
function useScrollEdges(
  /** Changes whenever the list's contents might have: measured again then. */
  contents: string,
) {
  const ref = useRef<HTMLElement>(null);
  const [edges, setEdges] = useState({ above: false, below: false });
  const measure = useCallback(() => {
    const el = ref.current;
    if (!el) return;
    const above = el.scrollTop > 1;
    const below = el.scrollTop + el.clientHeight < el.scrollHeight - 1;
    setEdges((was) => (was.above === above && was.below === below ? was : { above, below }));
  }, []);
  useEffect(() => {
    measure();
    window.addEventListener("resize", measure);
    // The list grows and shrinks as groups open and close, without the window changing.
    const observer = typeof ResizeObserver === "undefined" ? null : new ResizeObserver(measure);
    if (ref.current && observer) {
      observer.observe(ref.current);
      for (const child of Array.from(ref.current.children)) observer.observe(child);
    }
    return () => {
      window.removeEventListener("resize", measure);
      observer?.disconnect();
    };
  }, [measure, contents]);
  return { ref, edges, measure };
}

function SidebarContent({
  onNavigate,
  logoUrl,
  iconUrl = null,
  railed = false,
  onToggleRail,
}: {
  onNavigate?: () => void;
  logoUrl: string | null;
  /** The square mark the rail shows in place of the logo: the instance's header icon, when set. */
  iconUrl?: string | null;
  /** Icon-only rail. Desktop only - the mobile drawer is always full width. */
  railed?: boolean;
  onToggleRail?: () => void;
}) {
  const { session, signOut } = useSession();
  const navigate = useNavigate();
  const { pathname } = useLocation();
  // Navigation is the one place that still collapses: "show all the data"
  // is about data, and a nav group is a shelf, not a row of records.
  const [collapsed, setCollapsed] = useState<string[]>(loadCollapsed);
  // In the rail an icon is all there is, so its name shows beside it on hover and on focus -
  // the browser's own title tooltip came late, never on keyboard focus, and not to everyone.
  const [tip, setTip] = useState<{ label: string; top: number } | null>(null);
  const showTip = (el: HTMLElement, label: string) => {
    const r = el.getBoundingClientRect();
    setTip({ label, top: r.top + r.height / 2 });
  };
  const hideTip = () => setTip(null);
  const scroll = useScrollEdges(`${collapsed.join()}|${railed}|${session?.permissions.length ?? 0}`);
  if (!session) return null;

  const groups = visibleGroups(session.permissions);

  const toggleGroup = (label: string) =>
    setCollapsed((prev) => {
      const next = prev.includes(label) ? prev.filter((l) => l !== label) : [...prev, label];
      try {
        localStorage.setItem(COLLAPSED_KEY, JSON.stringify(next));
      } catch {
        // Storage refused (a private window): the choice still holds until the page reloads.
      }
      return next;
    });

  return (
    <div className="flex h-full flex-col">
      <div className={`flex items-center gap-2 pt-4 pb-2 ${railed ? "flex-col px-2" : "px-5"}`}>
        <Link to="/dashboard" className="min-w-0 flex-1">
          <img
            src={
              railed
                ? (iconUrl ?? import.meta.env.BASE_URL + "brand/BitweenIcon.svg")
                : (logoUrl ?? import.meta.env.BASE_URL + "brand/BitweenFull-light.svg")
            }
            alt="Bitween"
            className={railed ? "mx-auto h-6 w-6" : "h-6 w-fit"}
          />
        </Link>
        {onToggleRail && (
          <button
            onClick={onToggleRail}
            aria-label={railed ? "Expand navigation" : "Collapse navigation"}
            title={`${railed ? "Expand" : "Collapse"} navigation  [`}
            className="shrink-0 rounded-md p-1.5 text-ink-400 hover:bg-ink-900 hover:text-ink-100"
          >
            {railed ? <PanelLeftOpen className="size-4" /> : <PanelLeftClose className="size-4" />}
          </button>
        )}
      </div>

      {/* Rows of 26px and 8px between groups: all four groups, Administration included, fit a
          900px laptop screen with the browser's own bars taken off. Shorter windows scroll, and
          the fades at either end say there is more. */}
      <div className="relative flex min-h-0 flex-1 flex-col">
        <nav
          ref={scroll.ref}
          onScroll={scroll.measure}
          aria-label="Main"
          className={`min-h-0 flex-1 space-y-2 overflow-y-auto py-1 ${railed ? "px-2" : "px-3"}`}
        >
          {groups.map((group) => {
            // never hide the page the user is on
            const containsActive = group.items.some((item) => pathname.startsWith(item.path));
            const isCollapsed = collapsed.includes(group.label) && !containsActive;
            return (
              <div key={group.label}>
                {railed ? (
                  // The group label has nowhere to go in a 4rem rail, but the
                  // grouping still carries meaning - keep it as a rule.
                  <div className="mx-2 mb-1.5 border-t border-ink-900" aria-hidden />
                ) : (
                  <button
                    onClick={() => toggleGroup(group.label)}
                    aria-expanded={!isCollapsed}
                    className="flex w-full items-center justify-between rounded-md px-2.5 pt-0.5 pb-1 text-left hover:text-ink-300"
                  >
                    <span className="text-[11px] leading-4 font-semibold tracking-widest text-ink-400 uppercase">
                      {group.label}
                    </span>
                    {isCollapsed ? (
                      <ChevronRight className="size-3 text-ink-600" aria-hidden />
                    ) : (
                      <ChevronDown className="size-3 text-ink-600" aria-hidden />
                    )}
                  </button>
                )}
                {(railed || !isCollapsed) && (
                  <ul>
                    {group.items.map((item) => (
                      <li key={item.path}>
                        <NavLink
                          to={item.path}
                          onClick={onNavigate}
                          aria-label={railed ? item.label : undefined}
                          onMouseEnter={railed ? (e) => showTip(e.currentTarget, item.label) : undefined}
                          onMouseLeave={railed ? hideTip : undefined}
                          onFocus={railed ? (e) => showTip(e.currentTarget, item.label) : undefined}
                          onBlur={railed ? hideTip : undefined}
                          className={({ isActive }) =>
                            `group relative flex items-center rounded-lg py-[3px] text-sm transition-colors ${
                              railed ? "justify-center px-2" : "gap-2.5 px-2.5"
                            } ${
                              isActive
                                ? "bg-ink-800 font-medium text-white"
                                : "text-ink-300 hover:bg-ink-900 hover:text-ink-100"
                            }`
                          }
                        >
                          {({ isActive }) => (
                            <>
                              {isActive && (
                                <span
                                  className={`absolute inset-y-1 w-1 rounded-r-full bg-crimson-500 ${
                                    railed ? "-left-2" : "-left-3"
                                  }`}
                                />
                              )}
                              <item.icon className="size-4 shrink-0" aria-hidden />
                              {!railed && <span className="truncate">{item.label}</span>}
                            </>
                          )}
                        </NavLink>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            );
          })}
        </nav>
        <div
          aria-hidden
          className={`pointer-events-none absolute inset-x-0 top-0 h-6 bg-linear-to-b from-ink-950 to-transparent transition-opacity ${
            scroll.edges.above ? "opacity-100" : "opacity-0"
          }`}
        />
        <div
          aria-hidden
          className={`pointer-events-none absolute inset-x-0 bottom-0 h-8 bg-linear-to-t from-ink-950 to-transparent transition-opacity ${
            scroll.edges.below ? "opacity-100" : "opacity-0"
          }`}
        />
      </div>

      {railed && tip && (
        <div
          role="tooltip"
          style={{ top: tip.top }}
          className="pointer-events-none fixed left-[4.5rem] z-50 -translate-y-1/2 rounded-md bg-ink-800 px-2 py-1 text-[12px] font-medium whitespace-nowrap text-white shadow-lg"
        >
          {tip.label}
        </div>
      )}

      <div className={`border-t border-ink-900 ${railed ? "p-2" : "p-3"}`}>
        <Menu
          align="left"
          side="up"
          trigger={
            <button
              aria-label="Account menu"
              title={railed ? session.user.displayName : undefined}
              className={`flex w-full items-center rounded-lg py-1.5 text-left hover:bg-ink-900 ${
                railed ? "justify-center px-1" : "gap-2.5 px-2"
              }`}
            >
              <Avatar name={session.user.displayName} size="sm" />
              {!railed && (
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm font-medium text-ink-100">
                    {session.user.displayName}
                  </span>
                  <span className="block truncate text-[11px] text-ink-400">
                    {session.roles.map((r) => r.name).join(", ") || "No roles"}
                  </span>
                </span>
              )}
            </button>
          }
        >
          <MenuItem onSelect={() => navigate("/profile")}>
            <UserRound className="size-4" /> Your profile
          </MenuItem>
          <MenuItem onSelect={() => void signOut()}>
            <LogOut className="size-4" /> Sign out
          </MenuItem>
        </Menu>
      </div>
    </div>
  );
}

const RAILED_KEY = "bitween-nav-railed";

export function AppShell() {
  const [mobileOpen, setMobileOpen] = useState(false);
  const [railed, setRailed] = useState(() => localStorage.getItem(RAILED_KEY) === "1");
  const { pathname } = useLocation();
  // Pages' own names, once they have loaded what they show (see `usePageTitle`). Kept by the
  // address each was given for, so the next page never wears the last one's name, and a crumb
  // leading back up can still say which gateway or policy it leads to.
  const [names, setNames] = useState<Record<string, string>>({});
  const setNamed = useCallback(
    ({ path, title }: NamedPage) => setNames((was) => (was[path] === title ? was : { ...was, [path]: title })),
    [],
  );
  const current = useCurrentPage();
  const title = names[pathname] ?? null;
  // "Orders inbound · API gateways" on a page about one thing, "API gateways" on the list.
  const parent = current ? PAGES[current].parent : undefined;
  const tabTitle = current
    ? [title ?? PAGES[current].title, parent && PAGES[current].path.includes(":") ? PAGES[parent].title : null]
        .filter(Boolean)
        .join(" · ")
    : null;
  const branding = useApplyBranding(tabTitle);
  const [paletteOpen, setPaletteOpen] = useState(false);
  const closePalette = useCallback(() => setPaletteOpen(false), []);

  // Something opened by name goes on the palette's recent list.
  useEffect(() => {
    if (current && title) recordVisit({ path: pathname, title, kind: PAGES[current].title });
  }, [current, title, pathname]);

  // ⌘K / Ctrl+K anywhere, as in most tools people already use; "/" too, when not typing.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const typing =
        document.activeElement instanceof HTMLElement &&
        (document.activeElement.isContentEditable ||
          ["INPUT", "TEXTAREA", "SELECT"].includes(document.activeElement.tagName));
      const commandK = e.key.toLowerCase() === "k" && (e.metaKey || e.ctrlKey) && !e.altKey && !e.shiftKey;
      const slash = e.key === "/" && !typing && !e.metaKey && !e.ctrlKey && !e.altKey;
      if (!commandK && !slash) return;
      e.preventDefault();
      setPaletteOpen((open) => (commandK ? !open : true));
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);
  // A page that owns the whole viewport (the bus-gateway studio, the flow map) says so in the page
  // registry: the shell stops supplying padding, a footer and a page-level scroll. Known from the
  // matched route rather than announced by the page, which could only ask once it had mounted.
  const fullBleed = current !== null && PAGES[current].layout === "full";

  const toggleRail = () =>
    setRailed((prev) => {
      localStorage.setItem(RAILED_KEY, prev ? "0" : "1");
      return !prev;
    });

  // `[` toggles the rail. No modifier, so it has to stand down while the user is
  // typing - an operator editing a handler URL types brackets constantly.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "[" || e.metaKey || e.ctrlKey || e.altKey) return;
      const el = document.activeElement;
      if (
        el instanceof HTMLElement &&
        (el.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(el.tagName))
      )
        return;
      e.preventDefault();
      toggleRail();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  return (
    <div
      // Published as a variable so anything pinned across the content column -
      // the unsaved-changes bars - tracks the sidebar instead of hard-coding it.
      style={{ "--sidebar-w": railed ? "4rem" : "15.5rem" } as React.CSSProperties}
      className="min-h-screen lg:grid lg:grid-cols-[var(--sidebar-w)_1fr]"
    >
      {/* mobile top bar */}
      {/* The two buttons are 44px, the touch guideline, and the bar's padding gives way so the
          bar itself stays about as tall as it was: at 30px they were easy to miss with a thumb. */}
      <header className="sticky top-0 z-30 flex items-center gap-2 border-b border-ink-100 bg-white px-2 py-0.5 lg:hidden">
        <button
          onClick={() => setMobileOpen(true)}
          aria-label="Open navigation"
          className="inline-flex size-11 items-center justify-center rounded-md text-ink-600 hover:bg-ink-100"
        >
          <MenuIcon className="size-5" />
        </button>
        <Link to="/dashboard">
          <img
            // "Mobile header icon" wins here when it's been set; otherwise this keeps
            // falling back to the sidebar logo, so the default look is unchanged.
            src={
              branding.headerIconUrl ??
              branding.sidebarLogoUrl ??
              import.meta.env.BASE_URL + "brand/BitweenFull.svg"
            }
            alt="Bitween"
            className="h-5"
          />
        </Link>
        <span className="ml-auto flex items-center gap-2">
          <EnvironmentBadge branding={branding} />
          <button
            onClick={() => setPaletteOpen(true)}
            aria-label="Search"
            className="inline-flex size-11 items-center justify-center rounded-md text-ink-600 hover:bg-ink-100"
          >
            <SearchIcon className="size-5" />
          </button>
        </span>
      </header>
      {paletteOpen && <CommandPalette onClose={closePalette} />}

      {/* mobile drawer */}
      {mobileOpen && (
        <div className="fixed inset-0 z-40 lg:hidden">
          <div className="absolute inset-0 bg-ink-950/50" onClick={() => setMobileOpen(false)} />
          <div className="absolute inset-y-0 left-0 w-72 bg-ink-950 shadow-xl">
            <button
              onClick={() => setMobileOpen(false)}
              aria-label="Close navigation"
              className="absolute top-1.5 right-1 inline-flex size-11 items-center justify-center rounded-md text-ink-400 hover:text-white"
            >
              <X className="size-5" />
            </button>
            <SidebarContent onNavigate={() => setMobileOpen(false)} logoUrl={branding.sidebarLogoUrl} />
          </div>
        </div>
      )}

      {/* desktop sidebar */}
      <aside className="sticky top-0 hidden h-screen bg-ink-950 lg:block">
        <SidebarContent
          logoUrl={branding.sidebarLogoUrl}
          iconUrl={branding.headerIconUrl}
          railed={railed}
          onToggleRail={toggleRail}
        />
      </aside>

      {/* `lg:h-screen` + `overflow-hidden` on a full-bleed page: the workspace
          scrolls in its own regions, and a page-level scrollbar on top of that is
          how you end up scrolling the canvas out from under its own toolbar.
          Below `lg` it stays a normal stacking page - a three-pane studio on a
          phone is not a thing worth building. */}
      <main className={`flex min-w-0 flex-col ${fullBleed ? "lg:h-screen lg:overflow-hidden" : ""}`}>
        <TopBar page={current} title={title} names={names} branding={branding} onSearch={() => setPaletteOpen(true)} />
        <PageTitleContext.Provider value={setNamed}>
        {fullBleed ? (
          <>
            {/* Empty when there is no settings draft, and hidden with it -
                otherwise its padding steals a strip off the canvas. */}
            <div className="px-4 pt-4 empty:hidden sm:px-6">
              <SettingsPreviewBanner />
            </div>
            <Suspense fallback={<LoadingBlock />}>
              <Outlet />
            </Suspense>
          </>
        ) : (
          <>
            <div className="w-full px-4 py-4 sm:px-6 sm:py-5">
              <SettingsPreviewBanner />
              <Suspense fallback={<LoadingBlock />}>
                <Outlet />
              </Suspense>
            </div>
            <AppFooter branding={branding} />
          </>
        )}
        </PageTitleContext.Provider>
      </main>
    </div>
  );
}
