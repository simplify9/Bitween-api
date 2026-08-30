import { useEffect, useState } from "react";
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
} from "lucide-react";
import type { SettingRow } from "../../api";
import { useSession } from "../../auth/SessionContext";
import { type Branding, useApplyBranding } from "../../lib/branding";
import { settingsDraft, useSettingsDraft } from "../../lib/settingsDraft";
import { visibleGroups } from "../../nav";
import { Avatar } from "../ui/Avatar";
import { Button } from "../ui/basics";
import { Menu, MenuItem } from "../ui/overlays";
import { keys } from "../../api/queryKeys";

/**
 * Shown on every page while unsaved setting changes exist — the whole app
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
        — this is how the app will look once saved.
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
      <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-ink-400">
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
                className="font-medium text-ink-500 hover:text-crimson-700 hover:underline"
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

const loadCollapsed = (): string[] => {
  try {
    return JSON.parse(localStorage.getItem(COLLAPSED_KEY) ?? "[]") as string[];
  } catch {
    return [];
  }
};

function SidebarContent({
  onNavigate,
  logoUrl,
  railed = false,
  onToggleRail,
}: {
  onNavigate?: () => void;
  logoUrl: string | null;
  /** Icon-only rail. Desktop only — the mobile drawer is always full width. */
  railed?: boolean;
  onToggleRail?: () => void;
}) {
  const { session, signOut } = useSession();
  const navigate = useNavigate();
  const { pathname } = useLocation();
  // Navigation is the one place that still collapses: "show all the data"
  // is about data, and a nav group is a shelf, not a row of records.
  const [collapsed, setCollapsed] = useState<string[]>(loadCollapsed);
  if (!session) return null;

  const groups = visibleGroups(session.permissions);

  const toggleGroup = (label: string) =>
    setCollapsed((prev) => {
      const next = prev.includes(label) ? prev.filter((l) => l !== label) : [...prev, label];
      localStorage.setItem(COLLAPSED_KEY, JSON.stringify(next));
      return next;
    });

  return (
    <div className="flex h-full flex-col">
      <div className={`flex items-center gap-2 pt-6 pb-4 ${railed ? "flex-col px-2" : "px-5"}`}>
        <Link to="/dashboard" className="min-w-0 flex-1">
          <img
            src={
              railed
                ? import.meta.env.BASE_URL + "brand/BitweenIcon.svg"
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
            className="shrink-0 rounded-md p-1.5 text-ink-500 hover:bg-ink-900 hover:text-ink-100"
          >
            {railed ? <PanelLeftOpen className="size-4" /> : <PanelLeftClose className="size-4" />}
          </button>
        )}
      </div>

      <nav className={`flex-1 space-y-4 overflow-y-auto py-3 ${railed ? "px-2" : "px-3"}`}>
        {groups.map((group) => {
          // never hide the page the user is on
          const containsActive = group.items.some((item) => pathname.startsWith(item.path));
          const isCollapsed = collapsed.includes(group.label) && !containsActive;
          return (
            <div key={group.label}>
              {railed ? (
                // The group label has nowhere to go in a 4rem rail, but the
                // grouping still carries meaning — keep it as a rule.
                <div className="mx-2 mb-1.5 border-t border-ink-900" aria-hidden />
              ) : (
                <button
                  onClick={() => toggleGroup(group.label)}
                  aria-expanded={!isCollapsed}
                  className="flex w-full items-center justify-between rounded-md px-2.5 pb-1.5 text-left hover:text-ink-300"
                >
                  <span className="text-[11px] font-semibold tracking-widest text-ink-500 uppercase">
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
                <ul className="space-y-0.5">
                  {group.items.map((item) => (
                    <li key={item.path}>
                      <NavLink
                        to={item.path}
                        onClick={onNavigate}
                        title={railed ? item.label : undefined}
                        className={({ isActive }) =>
                          `group relative flex items-center rounded-lg py-1.5 text-sm transition-colors ${
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

/**
 * Pages that own the whole viewport instead of sitting in the shell's page frame.
 *
 * The bus-gateway studio is a three-region workspace — route list, canvas,
 * configuration — each with its own scrollbar, which only works if the shell
 * stops supplying padding, a footer and a page-level scroll. The flow map is the
 * same bargain with two regions. Matched here rather than announced by the page
 * itself: a page can only ask once it has mounted, and the frame would visibly
 * snap away on arrival.
 */
const FULL_BLEED = [/^\/bus-gateways\/\d+$/, /^\/flow$/];

export function AppShell() {
  const [mobileOpen, setMobileOpen] = useState(false);
  const [railed, setRailed] = useState(() => localStorage.getItem(RAILED_KEY) === "1");
  const branding = useApplyBranding();
  const { pathname } = useLocation();
  const fullBleed = FULL_BLEED.some((r) => r.test(pathname));

  const toggleRail = () =>
    setRailed((prev) => {
      localStorage.setItem(RAILED_KEY, prev ? "0" : "1");
      return !prev;
    });

  // `[` toggles the rail. No modifier, so it has to stand down while the user is
  // typing — an operator editing a handler URL types brackets constantly.
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
      // Published as a variable so anything pinned across the content column —
      // the unsaved-changes bars — tracks the sidebar instead of hard-coding it.
      style={{ "--sidebar-w": railed ? "4rem" : "15.5rem" } as React.CSSProperties}
      className="min-h-screen lg:grid lg:grid-cols-[var(--sidebar-w)_1fr]"
    >
      {/* mobile top bar */}
      <header className="sticky top-0 z-30 flex items-center gap-3 border-b border-ink-100 bg-white px-4 py-2.5 lg:hidden">
        <button
          onClick={() => setMobileOpen(true)}
          aria-label="Open navigation"
          className="rounded-md p-1.5 text-ink-600 hover:bg-ink-100"
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
      </header>

      {/* mobile drawer */}
      {mobileOpen && (
        <div className="fixed inset-0 z-40 lg:hidden">
          <div className="absolute inset-0 bg-ink-950/50" onClick={() => setMobileOpen(false)} />
          <div className="absolute inset-y-0 left-0 w-72 bg-ink-950 shadow-xl">
            <button
              onClick={() => setMobileOpen(false)}
              aria-label="Close navigation"
              className="absolute top-4 right-3 rounded-md p-1 text-ink-400 hover:text-white"
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
          railed={railed}
          onToggleRail={toggleRail}
        />
      </aside>

      {/* `lg:h-screen` + `overflow-hidden` on a full-bleed page: the workspace
          scrolls in its own regions, and a page-level scrollbar on top of that is
          how you end up scrolling the canvas out from under its own toolbar.
          Below `lg` it stays a normal stacking page — a three-pane studio on a
          phone is not a thing worth building. */}
      <main className={`flex min-w-0 flex-col ${fullBleed ? "lg:h-screen lg:overflow-hidden" : ""}`}>
        {fullBleed ? (
          <>
            {/* Empty when there is no settings draft, and hidden with it —
                otherwise its padding steals a strip off the canvas. */}
            <div className="px-4 pt-4 empty:hidden sm:px-6">
              <SettingsPreviewBanner />
            </div>
            <Outlet />
          </>
        ) : (
          <>
            <div className="w-full px-4 py-4 sm:px-6 sm:py-5">
              <SettingsPreviewBanner />
              <Outlet />
            </div>
            <AppFooter branding={branding} />
          </>
        )}
      </main>
    </div>
  );
}
