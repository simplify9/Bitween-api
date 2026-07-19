import { useEffect, useState, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { KeyRound, Trash2, UserRoundCheck, UserRoundX, X } from "lucide-react";
import { api } from "../../api";
import { Can } from "../../auth/guards";
import { useSession } from "../../auth/SessionContext";
import { Avatar } from "../../components/ui/Avatar";
import { Badge, Button, FormError, LoadingBlock } from "../../components/ui/basics";
import { Checkbox } from "../../components/ui/forms";
import { ConfirmDialog } from "../../components/ui/overlays";
import { CopyField } from "../../components/ui/CopyField";
import { formatDate, timeAgo } from "../../lib/dates";
import { statusBadge } from "./MembersTab";

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

  const user = useQuery({ queryKey: ["user", userId], queryFn: () => api.getUser(userId), retry: false });
  const roles = useQuery({ queryKey: ["roles"], queryFn: () => api.listRoles() });
  const invite = useQuery({
    queryKey: ["user-invite", userId],
    queryFn: () => api.getInviteForUser(userId),
    enabled: user.data?.status === "invited",
  });

  const [draftRoleIds, setDraftRoleIds] = useState<string[] | null>(null);
  const [resetLink, setResetLink] = useState("");
  const [confirming, setConfirming] = useState<"remove" | "revoke" | null>(null);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onClose]);

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ["users"] });
    void queryClient.invalidateQueries({ queryKey: ["user", userId] });
    void queryClient.invalidateQueries({ queryKey: ["roles"] });
  };

  const saveRoles = useMutation({
    mutationFn: (roleIds: string[]) => api.updateUserRoles(userId, roleIds),
    onSuccess: () => {
      setDraftRoleIds(null);
      invalidate();
    },
  });
  const setDisabled = useMutation({
    mutationFn: (disabled: boolean) => api.setUserDisabled(userId, disabled),
    onSuccess: invalidate,
  });
  const resetPassword = useMutation({
    mutationFn: () => api.adminResetPassword(userId),
    onSuccess: ({ resetLink: link }) => setResetLink(link),
  });
  const resend = useMutation({
    mutationFn: () => api.resendInvite(userId),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ["user-invite", userId] }),
  });

  const isSelf = session?.user.id === userId;
  const editable = can("users.edit");

  const body = () => {
    if (user.isPending) return <LoadingBlock label="Loading member…" />;
    if (user.isError)
      return <p className="px-5 py-8 text-sm text-ink-500">This member no longer exists.</p>;
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
            <div className="mt-1.5">{statusBadge(u.status)}</div>
          </div>
        </div>

        {u.status === "invited" && (
          <Section title="Pending invite">
            {invite.data ? (
              <div className="space-y-3">
                <p className="text-sm text-ink-600">
                  Invited by {invite.data.invitedByName} · expires {formatDate(invite.data.expiresOn)}.
                </p>
                <CopyField value={`${location.origin}${import.meta.env.BASE_URL}invite/${invite.data.token}`} label="Invite link" />
                {editable && (
                  <div className="flex gap-2">
                    <Button size="sm" busy={resend.isPending} onClick={() => resend.mutate()}>
                      Send a fresh link
                    </Button>
                    <Button size="sm" variant="danger" onClick={() => setConfirming("revoke")}>
                      Revoke invite
                    </Button>
                  </div>
                )}
              </div>
            ) : (
              <LoadingBlock label="Loading invite…" />
            )}
          </Section>
        )}

        <Section title="Roles">
          {editable ? (
            <div className="space-y-2.5">
              {(roles.data ?? []).map((role) => (
                <Checkbox
                  key={role.id}
                  label={role.name}
                  description={role.description}
                  checked={currentRoles.includes(role.id)}
                  onChange={() =>
                    setDraftRoleIds(
                      currentRoles.includes(role.id)
                        ? currentRoles.filter((r) => r !== role.id)
                        : [...currentRoles, role.id],
                    )
                  }
                />
              ))}
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
            <div className="flex justify-between gap-4">
              <dt className="text-ink-500">Phone</dt>
              <dd className="text-ink-800">{u.phone ?? "—"}</dd>
            </div>
            <div className="flex justify-between gap-4">
              <dt className="text-ink-500">Member since</dt>
              <dd className="text-ink-800">{formatDate(u.createdOn)}</dd>
            </div>
            <div className="flex justify-between gap-4">
              <dt className="text-ink-500">Last active</dt>
              <dd className="text-ink-800">{u.lastActiveOn ? timeAgo(u.lastActiveOn) : "Never"}</dd>
            </div>
          </dl>
        </Section>

        {u.status !== "invited" && (editable || can("users.delete")) && !isSelf && (
          <Section title="Account actions">
            <div className="space-y-3">
              {editable && u.status === "active" && (
                <div className="space-y-2">
                  <Button size="sm" busy={resetPassword.isPending} onClick={() => resetPassword.mutate()}>
                    <KeyRound className="size-3.5" /> Reset their password
                  </Button>
                  {resetLink && (
                    <div className="rounded-xl border border-dashed border-ink-300 p-3">
                      <p className="mb-2 text-[13px] text-ink-500">
                        Prototype: share this link — the email that would carry it isn't wired up.
                      </p>
                      <CopyField value={resetLink} />
                    </div>
                  )}
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
                    Disabled members can't sign in, but keep their history and roles.
                  </p>
                </div>
              )}
              <FormError>{setDisabled.error?.message}</FormError>
              <Can permission="users.delete">
                <Button size="sm" variant="danger" onClick={() => setConfirming("remove")}>
                  <Trash2 className="size-3.5" /> Remove from team
                </Button>
              </Can>
            </div>
          </Section>
        )}
      </>
    );
  };

  return (
    <div className="fixed inset-0 z-50">
      <div className="absolute inset-0 bg-ink-950/40" onClick={onClose} />
      <aside
        role="dialog"
        aria-modal="true"
        aria-label="Member details"
        className="absolute inset-y-0 right-0 flex w-full max-w-md flex-col overflow-y-auto bg-white shadow-2xl"
      >
        <button
          onClick={onClose}
          aria-label="Close"
          className="absolute top-4 right-4 rounded-md p-1 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
        >
          <X className="size-5" />
        </button>
        {body()}
      </aside>

      {confirming === "remove" && user.data && (
        <ConfirmDialog
          title="Remove this member?"
          body={
            <>
              <strong className="font-medium text-ink-800">{user.data.displayName}</strong> will lose
              access immediately. This can't be undone.
            </>
          }
          confirmLabel="Remove member"
          onConfirm={async () => {
            await api.deleteUser(userId);
            invalidate();
            onClose();
          }}
          onClose={() => setConfirming(null)}
        />
      )}
      {confirming === "revoke" && user.data && (
        <ConfirmDialog
          title="Revoke this invite?"
          body={
            <>
              The invite link for{" "}
              <strong className="font-medium text-ink-800">{user.data.email}</strong> will stop
              working and they'll disappear from the member list.
            </>
          }
          confirmLabel="Revoke invite"
          onConfirm={async () => {
            await api.revokeInvite(userId);
            invalidate();
            onClose();
          }}
          onClose={() => setConfirming(null)}
        />
      )}
    </div>
  );
}
