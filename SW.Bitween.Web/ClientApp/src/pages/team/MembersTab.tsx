import { useMemo } from "react";
import { useNavigate, useParams, useSearchParams } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { Search, UserPlus, UsersRound } from "lucide-react";
import { api, type User, type UserStatus } from "../../api";
import { Can } from "../../auth/guards";
import { Avatar } from "../../components/ui/Avatar";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { timeAgo } from "../../lib/dates";
import { AddMemberDialog } from "./AddMemberDialog";
import { MemberDrawer } from "./MemberDrawer";

const STATUS_FILTERS: { value: string; label: string }[] = [
  { value: "all", label: "All" },
  { value: "active", label: "Active" },
  { value: "disabled", label: "Disabled" },
];

export function statusBadge(status: UserStatus) {
  if (status === "active") return <Badge tone="ok">Active</Badge>;
  return <Badge tone="neutral">Disabled</Badge>;
}

export function MembersTab() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const { id: openMemberId } = useParams();

  const q = searchParams.get("q") ?? "";
  const status = searchParams.get("status") ?? "all";
  const addOpen = searchParams.get("add") === "1";

  const users = useQuery({ queryKey: ["users"], queryFn: () => api.listUsers() });
  const roles = useQuery({ queryKey: ["roles"], queryFn: () => api.listRoles() });

  const setParam = (key: string, value: string | null) => {
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        if (value) next.set(key, value);
        else next.delete(key);
        return next;
      },
      { replace: key === "q" },
    );
  };

  const roleName = (id: string) => roles.data?.find((r) => r.id === id)?.name ?? "…";

  const filtered = useMemo(() => {
    const needle = q.trim().toLowerCase();
    return (users.data ?? []).filter((u) => {
      if (status !== "all" && u.status !== status) return false;
      if (!needle) return true;
      return (
        u.displayName.toLowerCase().includes(needle) || u.email.toLowerCase().includes(needle)
      );
    });
  }, [users.data, q, status]);

  const openMember = (user: User) =>
    navigate(`/team/members/${user.id}?${searchParams.toString()}`);

  return (
    <div>
      <div className="mb-4 flex flex-wrap items-center gap-3">
        <div className="relative min-w-56 flex-1 sm:max-w-xs">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-400" />
          <input
            type="search"
            value={q}
            onChange={(e) => setParam("q", e.target.value || null)}
            placeholder="Search by name or email"
            aria-label="Search members"
            className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
          />
        </div>

        <div className="flex rounded-lg border border-ink-200 bg-white p-0.5">
          {STATUS_FILTERS.map((f) => (
            <button
              key={f.value}
              onClick={() => setParam("status", f.value === "all" ? null : f.value)}
              className={`rounded-md px-2.5 py-1 text-[13px] font-medium transition-colors ${
                status === f.value ? "bg-ink-800 text-white" : "text-ink-500 hover:text-ink-800"
              }`}
            >
              {f.label}
            </button>
          ))}
        </div>

        <div className="ml-auto">
          <Can permission="users.create">
            <Button variant="primary" onClick={() => setParam("add", "1")}>
              <UserPlus className="size-4" /> Add member
            </Button>
          </Can>
        </div>
      </div>

      {users.isPending ? (
        <LoadingBlock label="Loading members…" />
      ) : filtered.length === 0 ? (
        <EmptyState
          icon={<UsersRound />}
          title={q || status !== "all" ? "No members match" : "No members yet"}
          action={
            q || status !== "all" ? (
              <Button onClick={() => setSearchParams({})}>Clear filters</Button>
            ) : (
              <Can permission="users.create">
                <Button variant="primary" onClick={() => setParam("add", "1")}>
                  <UserPlus className="size-4" /> Add member
                </Button>
              </Can>
            )
          }
        >
          {q || status !== "all"
            ? "Try a different search or filter."
            : "Add the first person to your team."}
        </EmptyState>
      ) : (
        <div className="overflow-x-auto rounded-xl border border-ink-200 bg-white">
          <table className="w-full min-w-160 text-sm">
            <thead>
              <tr className="border-b border-ink-100 text-left text-xs text-ink-500">
                <th className="px-4 py-2.5 font-medium">Member</th>
                <th className="px-4 py-2.5 font-medium">Roles</th>
                <th className="px-4 py-2.5 font-medium">Status</th>
                <th className="px-4 py-2.5 font-medium">Last active</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((user) => (
                <tr
                  key={user.id}
                  onClick={() => openMember(user)}
                  className="cursor-pointer border-b border-ink-100 last:border-b-0 hover:bg-ink-50"
                >
                  <td className="px-4 py-2.5">
                    <div className="flex items-center gap-3">
                      <Avatar name={user.displayName} dimmed={user.status === "disabled"} />
                      <div className="min-w-0">
                        <p className={`truncate font-medium ${user.status === "disabled" ? "text-ink-400" : "text-ink-900"}`}>
                          {user.displayName}
                        </p>
                        <p className="truncate font-mono text-xs text-ink-500">{user.email}</p>
                      </div>
                    </div>
                  </td>
                  <td className="px-4 py-2.5">
                    <div className="flex flex-wrap gap-1">
                      {user.roleIds.map((rid) => (
                        <Badge key={rid}>{roleName(rid)}</Badge>
                      ))}
                    </div>
                  </td>
                  <td className="px-4 py-2.5">{statusBadge(user.status)}</td>
                  <td className="px-4 py-2.5 text-ink-500">
                    {user.lastActiveOn ? timeAgo(user.lastActiveOn) : "—"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {addOpen && <AddMemberDialog onClose={() => setParam("add", null)} />}
      {openMemberId && (
        <MemberDrawer
          userId={openMemberId}
          onClose={() => navigate(`/team/members?${searchParams.toString()}`)}
        />
      )}
    </div>
  );
}
