import { useEffect, useState, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, KeyRound, LockOpen, Trash2, UserRoundCheck, UserRoundX, X } from "lucide-react";
import { api } from "../../api";
import { Can } from "../../auth/guards";
import { useSession } from "../../auth/SessionContext";
import { Avatar } from "../../components/ui/Avatar";
import { CopyField } from "../../components/ui/CopyField";
import { Badge, Button, FormError, LoadingBlock } from "../../components/ui/basics";
import { Checkbox, PasswordInput } from "../../components/ui/forms";
import { ConfirmDialog } from "../../components/ui/overlays";
import { formatDate, timeAgo, timeUntil } from "../../lib/dates";
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
  const [draftRoleIds, setDraftRoleIds] = useState<string[] | null>(null);
  const [confirming, setConfirming] = useState<"remove" | null>(null);
  const [newPassword, setNewPassword] = useState("");
  // The password that was just set, kept on screen deliberately. Bitween sends no
  // mail, so the admin has to pass it on themselves — clearing the field on success
  // threw away the one thing they still needed, with nothing to say it had worked.
  const [issuedPassword, setIssuedPassword] = useState<string | null>(null);

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
  const unlock = useMutation({
    mutationFn: () => api.unlockUser(userId),
    onSuccess: invalidate,
  });

  const setPassword = useMutation({
    mutationFn: (password: string) => api.setUserPassword(userId, password),
    onSuccess: (_result, password) => {
      setIssuedPassword(password);
      setNewPassword("");
    },
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
            <div className="mt-1.5">{statusBadge(u.status, u.lockedUntil)}</div>
          </div>
        </div>

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
              <dt className="text-ink-500">Member since</dt>
              <dd className="text-ink-800">{formatDate(u.createdOn)}</dd>
            </div>
            <div className="flex justify-between gap-4">
              <dt className="text-ink-500">Last active</dt>
              <dd className="text-ink-800">{u.lastActiveOn ? timeAgo(u.lastActiveOn) : "Never"}</dd>
            </div>
          </dl>
        </Section>

        {(editable || can("users.delete")) && !isSelf && (
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
                    They can sign in with it now. Bitween sends no email, so copy it and pass it on
                    yourself — it won't be shown again once you dismiss this.
                  </p>
                  <div className="flex justify-end">
                    <Button size="sm" onClick={() => setIssuedPassword(null)}>
                      Done
                    </Button>
                  </div>
                </div>
              )}
              {editable && !issuedPassword && (
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
                    There's no reset email, so set it here and pass it on yourself.
                  </p>
                  <FormError>{setPassword.error?.message}</FormError>
                </form>
              )}
              {editable && u.lockedUntil && (
                <div>
                  <Button size="sm" busy={unlock.isPending} onClick={() => unlock.mutate()}>
                    <LockOpen className="size-3.5" /> Unlock account
                  </Button>
                  <p className="mt-1 text-[13px] text-ink-500">
                    Locked after repeated failed sign-ins, for another {timeUntil(u.lockedUntil)}.
                    Unlocking clears it now.
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
    </div>
  );
}
