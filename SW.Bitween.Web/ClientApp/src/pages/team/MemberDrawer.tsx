import { useEffect, useRef, useState, type ReactNode } from "react";
import { RolePicker } from "./RolePicker";
import { Link } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, KeyRound, LockOpen, Send, Trash2, UserRoundCheck, UserRoundX, X } from "lucide-react";
import { api, ApiRequestError, getAppConfig, type InviteResult, type User } from "../../api";
import { Can } from "../../auth/guards";
import { useSession } from "../../auth/useSession";
import { HistoryList } from "../../components/config/HistoryCard";
import { Avatar } from "../../components/ui/Avatar";
import { CopyField } from "../../components/ui/CopyField";
import { Badge, Button, FormError, LoadError, LoadingBlock } from "../../components/ui/basics";
import { isNotFound } from "../../components/ui/isNotFound";
import { PasswordInput } from "../../components/ui/forms";
import { ConfirmDialog } from "../../components/ui/overlays";
import { useModalFocus } from "../../components/ui/useModalFocus";
import { formatDate, formatDateTime, timeAgo, timeUntil } from "../../lib/dates";
import { inviteActive, statusBadge } from "./memberStatus";
import { ConnectedAppsList } from "./ConnectedApps";
import { keys } from "../../api/queryKeys";
import { rolesForSide, useGrantableRoles, useTeamSource } from "./teamSource";

/** Where a member's invite stands, in a sentence. */
function inviteLine(u: User, partner: boolean) {
  if (!u.invite) return "They have no password yet, and no invite has been made.";
  if (!inviteActive(u)) return `Their invite expired ${timeAgo(u.invite.expiresOn)}.`;
  // The portal doesn't say how it went out.
  if (partner) return `Invited. The link works until ${formatDateTime(u.invite.expiresOn)}.`;
  if (u.invite.emailedOn)
    return `Invite emailed ${timeAgo(u.invite.emailedOn)}. The link works until ${formatDateTime(u.invite.expiresOn)}.`;
  return `An invite link was made to send by hand. It works until ${formatDateTime(u.invite.expiresOn)}.`;
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="border-t border-ink-100 px-5 py-4">
      <h3 className="mb-3 text-xs font-semibold tracking-wide text-ink-500 uppercase">{title}</h3>
      {children}
    </section>
  );
}

export function MemberDrawer({ userId, onClose }: { userId: string; onClose: () => void }) {
  const { session, can } = useSession();
  const queryClient = useQueryClient();
  const source = useTeamSource();
  const partner = source.partner;
  const accountId = Number(userId);

  const user = useQuery({
    queryKey: partner ? [...source.keys.members, "one", userId] : keys.users.detail(userId),
    queryFn: async () => {
      if (!partner) return api.getUser(userId);
      // Read out of their company's list; someone not in it isn't there for them.
      const found = (await source.listMembers()).find((m) => m.id === userId);
      if (!found) throw new ApiRequestError("NOT_FOUND", "This person is no longer here.");
      return found;
    },
    retry: false,
  });
  const roles = useQuery({ queryKey: source.keys.roles, queryFn: () => source.listRoles() });
  const grantable = useGrantableRoles(user.data?.partner?.id ?? null);
  const appConfig = useQuery({ queryKey: keys.appConfig, queryFn: getAppConfig });
  const mailOn = Boolean(appConfig.data?.mailServerConfigured);
  const passwordsOff = Boolean(appConfig.data?.disableEmailPasswordLogin);
  const resetAvailable = Boolean(appConfig.data?.passwordResetAvailable);
  const [draftRoleIds, setDraftRoleIds] = useState<string[] | null>(null);
  // The invite just made: a link to pass on is shown once, as a password just set is.
  const [issuedInvite, setIssuedInvite] = useState<InviteResult | null>(null);
  const [confirming, setConfirming] = useState<"remove" | null>(null);
  const [newPassword, setNewPassword] = useState("");
  // The password that was just set, kept on screen deliberately. Bitween sends no
  // mail, so the admin has to pass it on themselves - clearing the field on success
  // threw away the one thing they still needed, with nothing to say it had worked.
  const [issuedPassword, setIssuedPassword] = useState<string | null>(null);

  const panel = useRef<HTMLElement>(null);
  useModalFocus(panel);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onClose]);

  const invalidate = () => {
    for (const key of source.keys.all) void queryClient.invalidateQueries({ queryKey: key });
  };

  const saveRoles = useMutation({
    mutationFn: async (roleIds: string[]) => {
      await (partner ? api.setPortalPersonRole(accountId, Number(roleIds[0])) : api.updateUserRoles(userId, roleIds));
    },
    onSuccess: () => {
      setDraftRoleIds(null);
      invalidate();
    },
  });
  const setDisabled = useMutation({
    mutationFn: async (disabled: boolean) => {
      await (partner ? api.setPortalPersonDisabled(accountId, disabled) : api.setUserDisabled(userId, disabled));
    },
    onSuccess: invalidate,
  });
  const unlock = useMutation({
    mutationFn: () => api.unlockUser(userId),
    onSuccess: invalidate,
  });

  const invite = useMutation({
    mutationFn: () => (partner ? api.invitePortalPersonAgain(accountId) : api.inviteUser(userId)),
    onSuccess: (result) => {
      setIssuedInvite(result);
      invalidate();
    },
  });

  const setPassword = useMutation({
    mutationFn: (password: string) => api.setUserPassword(userId, password),
    onSuccess: (_result, password) => {
      setIssuedPassword(password);
      setNewPassword("");
    },
  });

  const isSelf = session?.user.id === userId;
  // A partner's admin changes only whom the server says they may: not another admin, nobody beyond them.
  const editable = partner ? source.can.editMember && Boolean(user.data?.canChange) : can("users.edit");
  const canInvite = partner ? editable : can("users.create");
  const canRemove = partner ? editable : can("users.delete");

  const body = () => {
    if (user.isPending) return <LoadingBlock label="Loading member…" />;
    if (user.isError && !isNotFound(user.error))
      return (
        <div className="p-5">
          <LoadError error={user.error} what="this member" onRetry={() => void user.refetch()} />
        </div>
      );
    if (user.isError)
      return (
        <p className="px-5 py-8 text-sm text-ink-500">{partner ? "This person is no longer here." : "This member no longer exists."}</p>
      );
    const u = user.data;
    const currentRoles = draftRoleIds ?? u.roleIds;
    const rolesDirty =
      draftRoleIds !== null &&
      (draftRoleIds.length !== u.roleIds.length || draftRoleIds.some((r) => !u.roleIds.includes(r)));

    return (
      <>
        <div className="flex items-start gap-4 px-5 pt-5 pb-4">
          <Avatar name={u.displayName} size="lg" dimmed={u.status === "disabled"} />
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-2">
              <h2 className="truncate text-lg font-semibold tracking-tight text-ink-900">
                {u.displayName}
              </h2>
              {isSelf && <Badge tone="crimson">You</Badge>}
            </div>
            <p className="truncate font-mono text-[13px] text-ink-500">{u.email}</p>
            <div className="mt-1.5">{statusBadge(u, passwordsOff)}</div>
          </div>
        </div>

        {!u.hasPassword && (partner || !passwordsOff) && !isSelf && canInvite && (
          <Section title="Invite">
            <div className="space-y-2.5">
              <p className="text-sm text-ink-700">{inviteLine(u, partner)}</p>
              {u.invite?.emailError && !issuedInvite && (
                <FormError>The invite email couldn&apos;t be sent: {u.invite.emailError}</FormError>
              )}
              {issuedInvite?.emailed && (
                <p className="flex items-center gap-1.5 text-[13px] font-medium text-ok-800">
                  <Check className="size-4 shrink-0" aria-hidden />
                  Sent to {u.email}
                </p>
              )}
              {issuedInvite?.link && (
                <div className="space-y-2 rounded-lg border border-ok-200 bg-ok-50 p-3">
                  {issuedInvite.emailError && (
                    <FormError>The email couldn&apos;t be sent: {issuedInvite.emailError}</FormError>
                  )}
                  <CopyField value={issuedInvite.link} label="Invite link" />
                  <p className="text-[13px] text-ink-600">
                    Send it to them yourself. It works once, and Bitween keeps no copy, so it won&apos;t be
                    shown again once you close this.
                  </p>
                </div>
              )}
              <Button size="sm" busy={invite.isPending} onClick={() => invite.mutate()}>
                <Send className="size-3.5" />
                {partner ? "Send invite again" : mailOn ? (u.invite ? "Resend invite" : "Send invite") : "Make a new invite link"}
              </Button>
              <p className="text-[13px] text-ink-500">
                A new invite stops the earlier link working.
              </p>
              <FormError>{invite.error?.message}</FormError>
            </div>
          </Section>
        )}

        <Section title={partner ? "Role" : "Roles"}>
          {editable ? (
            <div className="space-y-2.5">
              {/* Each side holds its own roles only, so only those are offered. */}
              <RolePicker
                legend="Their role"
                roles={partner ? (grantable.data ?? []) : rolesForSide(roles.data ?? [], u.partner?.id ?? null)}
                value={currentRoles}
                onChange={(roleId) => setDraftRoleIds([roleId])}
              />
              <FormError>{saveRoles.error?.message}</FormError>
              {rolesDirty && (
                <div className="flex gap-2 pt-1">
                  <Button size="sm" variant="primary" busy={saveRoles.isPending} onClick={() => saveRoles.mutate(draftRoleIds!)}>
                    Save roles
                  </Button>
                  <Button size="sm" onClick={() => setDraftRoleIds(null)}>
                    Discard
                  </Button>
                </div>
              )}
            </div>
          ) : (
            <div className="flex flex-wrap gap-1.5">
              {u.roleIds.map((rid) => (
                <Badge key={rid}>{roles.data?.find((r) => r.id === rid)?.name ?? "…"}</Badge>
              ))}
            </div>
          )}
        </Section>

        <Section title="Details">
          <dl className="space-y-2 text-sm">
            {!partner && (
            <div className="flex justify-between gap-4">
              <dt
                className="text-ink-500"
                title="Our team signs in to Bitween; a partner's people sign in to the partner portal and see only that partner's data. Fixed when they were added."
              >
                Belongs to
              </dt>
              <dd className="text-ink-800">{u.partner ? (u.partner.name ?? "Deleted partner") : "Our team"}</dd>
            </div>
            )}
            {u.createdOn && (
              <div className="flex justify-between gap-4">
                <dt className="text-ink-500">{partner ? "Added" : "Member since"}</dt>
                <dd className="text-ink-800">{formatDate(u.createdOn)}</dd>
              </div>
            )}
            <div className="flex justify-between gap-4">
              <dt className="text-ink-500">Last signed in</dt>
              <dd className="text-ink-800">{u.lastActiveOn ? timeAgo(u.lastActiveOn) : "Never"}</dd>
            </div>
          </dl>
        </Section>

        {/* A partner's people reach only the portal, never an app connected to Bitween. */}
        {!partner && (
          <Section title="Connected apps">
            <ConnectedAppsList accountId={u.id} canDisconnect={editable || isSelf} />
          </Section>
        )}

        {(editable || canRemove) && !isSelf && (
          <Section title="Account actions">
            <div className="space-y-3">
              {editable && issuedPassword && (
                <div className="space-y-2 rounded-lg border border-ok-200 bg-ok-50 p-3">
                  <p className="flex items-center gap-1.5 text-[13px] font-medium text-ok-800">
                    <Check className="size-4 shrink-0" aria-hidden />
                    Password set for {u.displayName}
                  </p>
                  <CopyField value={issuedPassword} label="New password" />
                  <p className="text-[13px] text-ink-600">
                    Copy it and pass it on yourself - it won't be shown again once you dismiss this.
                  </p>
                  {u.lockedUntil ? (
                    <div className="space-y-2 rounded-md border border-warn-400 bg-warn-100 p-2.5">
                      <p className="text-[13px] text-warn-700">
                        They still can't sign in. The account is locked after repeated failed
                        sign-ins, and a new password doesn't clear that - this is the other half.
                      </p>
                      <Button size="sm" busy={unlock.isPending} onClick={() => unlock.mutate()}>
                        <LockOpen className="size-3.5" /> Unlock account
                      </Button>
                      <FormError>{unlock.error?.message}</FormError>
                    </div>
                  ) : u.status === "disabled" ? (
                    <div className="space-y-2 rounded-md border border-warn-400 bg-warn-100 p-2.5">
                      <p className="text-[13px] text-warn-700">
                        They still can't sign in. The account is disabled, and a new password
                        doesn't change that - re-enable it below to let them back in.
                      </p>
                    </div>
                  ) : (
                    <p className="text-[13px] text-ink-600">They can sign in with it now.</p>
                  )}
                  <div className="flex justify-end">
                    <Button size="sm" onClick={() => setIssuedPassword(null)}>
                      Done
                    </Button>
                  </div>
                </div>
              )}
              {/* Their admin only invites: they never choose a password for someone. */}
              {editable && !partner && !issuedPassword && (
                <form
                  className="space-y-2"
                  onSubmit={(e) => {
                    e.preventDefault();
                    setPassword.mutate(newPassword);
                  }}
                >
                  <div className="flex gap-2">
                    <PasswordInput
                      required
                      minLength={8}
                      value={newPassword}
                      onChange={(e) => setNewPassword(e.target.value)}
                      placeholder="New password"
                      aria-label="New password"
                    />
                    <Button size="sm" type="submit" busy={setPassword.isPending}>
                      <KeyRound className="size-3.5" /> Set password
                    </Button>
                  </div>
                  <p className="text-[13px] text-ink-500">
                    {resetAvailable
                      ? "Pass it on yourself. They can also reset it themselves with Forgot password on the sign-in page."
                      : "There's no reset email, so set it here and pass it on yourself."}
                  </p>
                  <FormError>{setPassword.error?.message}</FormError>
                </form>
              )}
              {editable && !issuedPassword && u.lockedUntil && (
                <div>
                  <Button size="sm" busy={unlock.isPending} onClick={() => unlock.mutate()}>
                    <LockOpen className="size-3.5" /> Unlock account
                  </Button>
                  <p className="mt-1 text-[13px] text-ink-500">
                    Locked after repeated failed sign-ins; it clears on its own{" "}
                    {timeUntil(u.lockedUntil)}. Unlocking clears it now - setting a password does
                    not.
                  </p>
                  <FormError>{unlock.error?.message}</FormError>
                </div>
              )}
              {editable && (
                <div>
                  <Button
                    size="sm"
                    busy={setDisabled.isPending}
                    onClick={() => setDisabled.mutate(u.status === "active")}
                  >
                    {u.status === "active" ? (
                      <>
                        <UserRoundX className="size-3.5" /> Disable account
                      </>
                    ) : (
                      <>
                        <UserRoundCheck className="size-3.5" /> Re-enable account
                      </>
                    )}
                  </Button>
                  <p className="mt-1 text-[13px] text-ink-500">
                    {partner
                      ? "They can't sign in while disabled, and keep their role."
                      : "Disabled members can't sign in, but keep their history and roles."}
                  </p>
                </div>
              )}
              <FormError>{setDisabled.error?.message}</FormError>
              {canRemove && (
                <Button size="sm" variant="danger" onClick={() => setConfirming("remove")}>
                  <Trash2 className="size-3.5" /> {partner ? "Remove" : "Remove from team"}
                </Button>
              )}
            </div>
          </Section>
        )}

        <Can permission="audit.view">
          <Section title="History">
            <HistoryList entityName="Account" entityKey={userId} />
            {/* The drawer answers "what happened to this member"; the more useful question
                about a person is usually the other one, which the trail can also answer. */}
            <Link
              to={`/audit?userId=${userId}`}
              className="mt-2 inline-block text-[13px] font-medium link"
            >
              What this member changed
            </Link>
          </Section>
        </Can>
      </>
    );
  };

  return (
    <div className="fixed inset-0 z-50">
      <div className="absolute inset-0 bg-ink-950/40" onClick={onClose} />
      <aside
        ref={panel}
        tabIndex={-1}
        role="dialog"
        aria-modal="true"
        aria-label={partner ? "Person details" : "Member details"}
        className="absolute inset-y-0 right-0 flex w-full max-w-md flex-col overflow-y-auto bg-white shadow-2xl outline-none"
      >
        <button
          onClick={onClose}
          aria-label="Close"
          className="absolute top-4 right-4 rounded-md p-1 text-ink-500 hover:bg-ink-100 hover:text-ink-700"
        >
          <X className="size-5" />
        </button>
        {body()}
      </aside>

      {confirming === "remove" && user.data && (
        <ConfirmDialog
          title={partner ? "Remove this person?" : "Remove this member?"}
          body={
            <>
              <strong className="font-medium text-ink-800">{user.data.displayName}</strong> will lose
              access immediately. This can't be undone.
            </>
          }
          confirmLabel="Remove member"
          onConfirm={async () => {
            await (partner ? api.removePortalPerson(accountId) : api.deleteUser(userId));
            invalidate();
            onClose();
          }}
          onClose={() => setConfirming(null)}
        />
      )}
    </div>
  );
}
