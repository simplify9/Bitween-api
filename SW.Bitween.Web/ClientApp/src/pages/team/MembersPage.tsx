import { useMemo } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { UserPlus, UsersRound } from "lucide-react";
import { getAppConfig, type User } from "../../api";
import { useSession } from "../../auth/useSession";
import { PageHeader } from "../../components/layout/PageHeader";
import { Avatar } from "../../components/ui/Avatar";
import { clickableRow, clickableRowClass } from "../../components/ui/clickableRow";
import { Badge, Button, EmptyState, LoadError, LoadingBlock } from "../../components/ui/basics";
import { timeAgo } from "../../lib/dates";
import { AddMemberDialog } from "./AddMemberDialog";
import { MemberDrawer } from "./MemberDrawer";
import { keys } from "../../api/queryKeys";
import { useSearchText } from "../../lib/useSearchText";
import { statusBadge } from "./memberStatus";
import { SearchBox } from "../../components/ui/SearchBox";
import { CardList } from "../../components/ui/RowCard";
import { usePhone } from "../../lib/useMediaQuery";
import { usePageTitle } from "../../lib/pageTitle";
import { useTeamSource } from "./teamSource";

const STATUS_FILTERS: { value: string; label: string }[] = [
  { value: "all", label: "All" },
  { value: "active", label: "Active" },
  { value: "disabled", label: "Disabled" },
];

export function MembersPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const { id: openMemberId } = useParams();

  const q = searchParams.get("q") ?? "";
  const status = searchParams.get("status") ?? "all";
  const addOpen = searchParams.get("add") === "1";

  const source = useTeamSource();
  const users = useQuery({ queryKey: source.keys.members, queryFn: () => source.listMembers() });
  const roles = useQuery({ queryKey: source.keys.roles, queryFn: () => source.listRoles() });
  const appConfig = useQuery({ queryKey: keys.appConfig, queryFn: getAppConfig });
  const passwordsOff = Boolean(appConfig.data?.disableEmailPasswordLogin);
  const { can } = useSession();
  const phone = usePhone();
  // The open member's drawer has its own address, so the breadcrumb names them.
  usePageTitle(openMemberId ? users.data?.find((u) => u.id === openMemberId)?.displayName : null);

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
  const [searchText, setSearchText] = useSearchText(q, (text) => setParam("q", text || null));

  const roleName = (id: string) => roles.data?.find((r) => r.id === id)?.name ?? "…";

  const filtered = useMemo(() => {
    const needle = q.trim().toLowerCase();
    return (users.data ?? []).filter((u) => {
      if (status !== "all" && u.status !== status) return false;
      if (!needle) return true;
      return (
        u.displayName.toLowerCase().includes(needle) ||
        u.email.toLowerCase().includes(needle) ||
        (u.partner?.name ?? "").toLowerCase().includes(needle)
      );
    });
  }, [users.data, q, status]);

  const openMember = (user: User) =>
    navigate(`${source.membersPath}/${user.id}?${searchParams.toString()}`);
  const addLabel = source.partner ? "Invite someone" : "Add member";

  return (
    <div>
      <PageHeader
        title={source.partner ? "People" : "Members"}
        description={
          source.partner
            ? "Who at your company can sign in, and with which role. Only we can make or change one of your admins."
            : "Everyone who can sign in to this Bitween instance, and what each of them is allowed to do."
        }
        help={source.partner ? undefined : {
          title: "How access works",
          body: (
            <>
              <p>
                Every member holds one or more <strong>roles</strong>, and every role is a list of{" "}
                <strong>permissions</strong> - page by page, action by action. A member can do
                something if any of their roles allows it; everything else is hidden from them.
              </p>
              <p>
                To bring someone in: create or pick a role under <strong>Roles</strong>, then add
                them here with an invite. They choose their own password from a link: Bitween emails
                it once a mail server is set up in Settings, and otherwise gives it to you to send.
              </p>
            </>
          ),
        }}
      />

      <div className="mb-4 flex flex-wrap items-center gap-3">
        <SearchBox
          value={searchText}
          onChange={setSearchText}
          label={source.partner ? "Search people" : "Search members"}
          placeholder="Search by name or email"
          className="min-w-56 flex-1 sm:max-w-xs"
        />

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
          {source.can.addMember && (
            <Button variant="primary" onClick={() => setParam("add", "1")}>
              <UserPlus className="size-4" /> {addLabel}
            </Button>
          )}
        </div>
      </div>

      {/* Without a mail server an invite is a link the admin sends by hand, and nobody can reset a
          forgotten password themselves. Said once, here, where people are added. */}
      {appConfig.data && !appConfig.data.mailServerConfigured && source.can.addMember && (
        <p className="mb-4 rounded-lg border border-ink-200 bg-ink-50 px-3 py-2 text-[13px] text-ink-600">
          {source.partner
            ? "We can't send emails yet, so an invite gives you a link to send yourself."
            : "Bitween can't email members yet, so invites give you a link to send yourself, and there's no Forgot password on the sign-in page."}{" "}
          {can("settings.view") && (
            <Link to="/settings?section=Mail%20server" className="font-medium link">
              Set up a mail server
            </Link>
          )}
        </p>
      )}

      {users.isPending ? (
        <LoadingBlock label={source.partner ? "Loading people…" : "Loading members…"} />
      ) : users.isError ? (
        <LoadError error={users.error} what={source.partner ? "your company's people" : "members"} onRetry={() => void users.refetch()} />
      ) : filtered.length === 0 ? (
        <EmptyState
          icon={<UsersRound />}
          title={q || status !== "all" ? (source.partner ? "Nobody matches" : "No members match") : source.partner ? "Nobody yet" : "No members yet"}
          action={
            q || status !== "all" ? (
              <Button onClick={() => setSearchParams({})}>Clear filters</Button>
            ) : (
              source.can.addMember && (
                <Button variant="primary" onClick={() => setParam("add", "1")}>
                  <UserPlus className="size-4" /> {addLabel}
                </Button>
              )
            )
          }
        >
          {q || status !== "all"
            ? "Try a different search or filter."
            : source.partner
              ? "Invite the first person at your company."
              : "Add the first person to your team."}
        </EmptyState>
      ) : phone ? (
        <CardList
          rows={filtered}
          rowKey={(u) => u.id}
          label={source.partner ? "People" : "Members"}
          onRowClick={openMember}
          card={(user) => ({
            title: (
              <span className="flex items-center gap-2.5">
                <Avatar name={user.displayName} dimmed={user.status === "disabled"} />
                <span className={user.status === "disabled" ? "text-ink-500" : undefined}>{user.displayName}</span>
              </span>
            ),
            status: statusBadge(user, passwordsOff),
            technical: user.email,
            subtitle: (
              <span className="flex flex-wrap items-center gap-1">
                {!source.partner && <PartnerBadge user={user} />}
                {user.roleIds.map((rid) => (
                  <Badge key={rid}>{roleName(rid)}</Badge>
                ))}
                <span className="text-[12px] text-ink-500">
                  {user.lastActiveOn ? `signed in ${timeAgo(user.lastActiveOn)}` : "never signed in"}
                </span>
              </span>
            ),
          })}
        />
      ) : (
        <div className="overflow-x-auto rounded-xl border border-ink-200 bg-white">
          <table className="w-full min-w-160 text-sm">
            <thead>
              <tr className="border-b border-ink-100 text-left text-xs text-ink-500">
                <th className="px-4 py-2.5 font-medium">{source.partner ? "Person" : "Member"}</th>
                {!source.partner && (
                  <th
                    className="px-4 py-2.5 font-medium"
                    title="Our team signs in to Bitween; a partner's people sign in to the partner portal and see only that partner's data."
                  >
                    Belongs to
                  </th>
                )}
                <th className="px-4 py-2.5 font-medium" title="What they can see and do">
                  {source.partner ? "Role" : "Roles"}
                </th>
                <th className="px-4 py-2.5 font-medium">Status</th>
                <th className="px-4 py-2.5 font-medium">Last signed in</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((user) => (
                <tr
                  key={user.id}
                  {...clickableRow(() => openMember(user))}
                  className={`cursor-pointer border-b border-ink-100 last:border-b-0 hover:bg-ink-50 ${clickableRowClass}`}
                >
                  <td className="px-4 py-2.5">
                    <div className="flex items-center gap-3">
                      <Avatar name={user.displayName} dimmed={user.status === "disabled"} />
                      <div className="min-w-0">
                        <p className={`truncate font-medium ${user.status === "disabled" ? "text-ink-500" : "text-ink-900"}`}>
                          {user.displayName}
                        </p>
                        <p className="truncate font-mono text-xs text-ink-500">{user.email}</p>
                      </div>
                    </div>
                  </td>
                  {!source.partner && (
                    <td className="px-4 py-2.5">
                      {user.partner ? <PartnerBadge user={user} /> : <span className="text-ink-500">Our team</span>}
                    </td>
                  )}
                  <td className="px-4 py-2.5">
                    <div className="flex flex-wrap gap-1">
                      {user.roleIds.map((rid) => (
                        <Badge key={rid}>{roleName(rid)}</Badge>
                      ))}
                    </div>
                  </td>
                  <td className="px-4 py-2.5">{statusBadge(user, passwordsOff)}</td>
                  <td className="px-4 py-2.5 text-ink-500">
                    {user.lastActiveOn ? timeAgo(user.lastActiveOn) : "-"}
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
          // Keyed so switching straight from one member to another remounts the
          // drawer. Without it, per-member state (staged role edits, a password
          // just set) carries over and is shown under the next member's name.
          key={openMemberId}
          userId={openMemberId}
          onClose={() => navigate(`${source.membersPath}?${searchParams.toString()}`)}
        />
      )}
    </div>
  );
}

/** Whose person a partner's account is; nothing for our own people. */
function PartnerBadge({ user }: { user: User }) {
  if (!user.partner) return null;
  return (
    <Badge tone="ink" title="A partner's person: signs in to the partner portal and sees only this partner's data.">
      {user.partner.name ?? "Deleted partner"}
    </Badge>
  );
}
