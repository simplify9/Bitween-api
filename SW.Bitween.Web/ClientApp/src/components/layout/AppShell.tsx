import { useEffect, useState } from "react";
import { Link, NavLink, Outlet, useLocation, useNavigate } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { ChevronDown, ChevronRight, LogOut, Menu as MenuIcon, UserRound, X } from "lucide-react";
import { api } from "../../api";
import { useSession } from "../../auth/SessionContext";
import { applyColorScale } from "../../lib/colorScale";
import { visibleGroups } from "../../nav";
import { Avatar } from "../ui/Avatar";
import { Menu, MenuItem } from "../ui/overlays";
import { DemoSwitcher } from "./DemoSwitcher";

/** Applies the "Theme.PrimaryColor" setting as CSS vars so a rebrand shows up instantly, everywhere. */
function useLiveBrandColor() {
  const { data } = useQuery({ queryKey: ["settings"], queryFn: () => api.listSettings() });
  useEffect(() => {
    const row = data?.find((s) => s.key === "Theme.PrimaryColor");
    if (row) applyColorScale(row.value ?? row.defaultValue);
  }, [data]);
}

const COLLAPSED_KEY = "bitween-nav-collapsed";

const loadCollapsed = (): string[] => {
  try {
    return JSON.parse(localStorage.getItem(COLLAPSED_KEY) ?? "[]") as string[];
  } catch {
    return [];
  }
};

function SidebarContent({ onNavigate }: { onNavigate?: () => void }) {
  const { session, signOut } = useSession();
  const navigate = useNavigate();
  const { pathname } = useLocation();
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
      <div className="px-5 pt-6 pb-4">
        <Link to="/dashboard">
          <img src={import.meta.env.BASE_URL + "brand/BitweenFull-light.svg"} alt="Bitween" className="h-6 w-fit" />
        </Link>
      </div>

      <nav className="flex-1 space-y-4 overflow-y-auto px-3 py-3">
        {groups.map((group) => {
          // never hide the page the user is on
          const containsActive = group.items.some((item) => pathname.startsWith(item.path));
          const isCollapsed = collapsed.includes(group.label) && !containsActive;
          return (
            <div key={group.label}>
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
              {!isCollapsed && (
                <ul className="space-y-0.5">
                  {group.items.map((item) => (
                    <li key={item.path}>
                      <NavLink
                        to={item.path}
                        onClick={onNavigate}
                        className={({ isActive }) =>
                          `group relative flex items-center gap-2.5 rounded-lg px-2.5 py-1.5 text-sm transition-colors ${
                            isActive
                              ? "bg-ink-800 font-medium text-white"
                              : "text-ink-300 hover:bg-ink-900 hover:text-ink-100"
                          }`
                        }
                      >
                        {({ isActive }) => (
                          <>
                            {isActive && (
                              <span className="absolute inset-y-1 -left-3 w-1 rounded-r-full bg-crimson-500" />
                            )}
                            <item.icon className="size-4 shrink-0" aria-hidden />
                            <span className="truncate">{item.label}</span>
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

      <div className="border-t border-ink-900 p-3">
        <Menu
          align="left"
          side="up"
          trigger={
            <button className="flex w-full items-center gap-2.5 rounded-lg px-2 py-1.5 text-left hover:bg-ink-900">
              <Avatar name={session.user.displayName} size="sm" />
              <span className="min-w-0 flex-1">
                <span className="block truncate text-sm font-medium text-ink-100">
                  {session.user.displayName}
                </span>
                <span className="block truncate text-[11px] text-ink-400">
                  {session.roles.map((r) => r.name).join(", ") || "No roles"}
                </span>
              </span>
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

export function AppShell() {
  const [mobileOpen, setMobileOpen] = useState(false);
  useLiveBrandColor();

  return (
    <div className="min-h-screen lg:grid lg:grid-cols-[248px_1fr]">
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
          <img src={import.meta.env.BASE_URL + "brand/BitweenFull.svg"} alt="Bitween" className="h-5" />
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
            <SidebarContent onNavigate={() => setMobileOpen(false)} />
          </div>
        </div>
      )}

      {/* desktop sidebar */}
      <aside className="sticky top-0 hidden h-screen bg-ink-950 lg:block">
        <SidebarContent />
      </aside>

      <main className="min-w-0">
        <div className="mx-auto max-w-350 px-4 py-6 sm:px-8 sm:py-8">
          <Outlet />
        </div>
      </main>

      <DemoSwitcher />
    </div>
  );
}
