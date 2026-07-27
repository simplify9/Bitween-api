import { useState, type FormEvent, type ReactNode } from "react";
import { Link } from "react-router";
import { useMutation } from "@tanstack/react-query";
import { Check, ShieldCheck } from "lucide-react";
import { api } from "../api";
import { allKeysIn, usePermissionCatalog } from "../api/permissions";
import { useSession } from "../auth/SessionContext";
import { PageHeader } from "../components/layout/PageHeader";
import { Avatar } from "../components/ui/Avatar";
import { Badge, Button, FormError } from "../components/ui/basics";
import { Field, PasswordInput, TextInput } from "../components/ui/forms";

function Card({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="rounded-xl border border-ink-200 bg-white p-5">
      <h2 className="mb-4 text-[15px] font-semibold text-ink-900">{title}</h2>
      {children}
    </section>
  );
}

function SavedFlash({ show }: { show: boolean }) {
  if (!show) return null;
  return (
    <span className="inline-flex items-center gap-1 text-[13px] font-medium text-ok-600">
      <Check className="size-3.5" /> Saved
    </span>
  );
}

export function ProfilePage() {
  const { session, refresh, can } = useSession();
  const totalPermissions = allKeysIn(usePermissionCatalog().data ?? []).length;

  const [displayName, setDisplayName] = useState(session?.user.displayName ?? "");
  const [identitySaved, setIdentitySaved] = useState(false);

  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [passwordError, setPasswordError] = useState("");
  const [passwordSaved, setPasswordSaved] = useState(false);

  const identity = useMutation({
    mutationFn: () => api.updateProfile({ displayName }),
    onSuccess: async () => {
      await refresh();
      setIdentitySaved(true);
      setTimeout(() => setIdentitySaved(false), 2500);
    },
  });

  const password = useMutation({
    mutationFn: () => api.changePassword(currentPassword, newPassword),
    onSuccess: () => {
      setCurrentPassword("");
      setNewPassword("");
      setConfirmPassword("");
      setPasswordSaved(true);
      setTimeout(() => setPasswordSaved(false), 2500);
    },
  });

  if (!session) return null;
  const { user, roles, permissions } = session;

  const identityDirty = displayName !== user.displayName;

  const submitIdentity = (e: FormEvent) => {
    e.preventDefault();
    identity.mutate();
  };

  const submitPassword = (e: FormEvent) => {
    e.preventDefault();
    setPasswordError("");
    if (newPassword !== confirmPassword) {
      setPasswordError("The new passwords don't match.");
      return;
    }
    password.mutate();
  };

  return (
    <div>
      <PageHeader title="Your profile" description="How you appear to teammates, and how you sign in." />

      <div className="grid max-w-4xl gap-5 lg:grid-cols-2">
        <div className="space-y-5">
          <Card title="Identity">
            <div className="mb-5 flex items-center gap-4">
              <Avatar name={user.displayName} size="lg" />
              <div className="min-w-0">
                <p className="truncate font-semibold text-ink-900">{user.displayName}</p>
                <p className="truncate font-mono text-[13px] text-ink-500">{user.email}</p>
              </div>
            </div>
            <form onSubmit={submitIdentity} className="space-y-4">
              <Field label="Display name" htmlFor="pf-name">
                <TextInput id="pf-name" value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
              </Field>
              <Field label="Email" htmlFor="pf-email" hint="Your sign-in identity. Ask an administrator to change it.">
                <TextInput id="pf-email" value={user.email} disabled />
              </Field>
              <FormError>{identity.error?.message}</FormError>
              <div className="flex items-center gap-3">
                <Button type="submit" variant="primary" busy={identity.isPending} disabled={!identityDirty}>
                  Save changes
                </Button>
                <SavedFlash show={identitySaved} />
              </div>
            </form>
          </Card>

          <Card title="Your access">
            <ul className="space-y-2.5">
              {roles.map((role) => (
                <li key={role.id} className="flex items-start gap-2.5">
                  <ShieldCheck className="mt-0.5 size-4 shrink-0 text-crimson-600" />
                  <div className="min-w-0">
                    <p className="text-sm font-medium text-ink-800">{role.name}</p>
                  </div>
                </li>
              ))}
            </ul>
            <p className="mt-4 font-mono text-xs text-ink-500">
              {permissions.length}/{totalPermissions} permissions via {roles.length} role
              {roles.length === 1 ? "" : "s"}
            </p>
            {can("roles.view") && (
              <p className="mt-2 text-[13px] text-ink-500">
                See what each role unlocks under{" "}
                <Link to="/team/roles" className="font-medium text-crimson-700 hover:underline">
                  Team → Roles
                </Link>
                .
              </p>
            )}
          </Card>
        </div>

        <div className="space-y-5">
          <Card title="Password">
            <form onSubmit={submitPassword} className="space-y-4">
              <Field label="Current password" htmlFor="pf-current">
                <PasswordInput
                  id="pf-current"
                  autoComplete="current-password"
                  required
                  value={currentPassword}
                  onChange={(e) => setCurrentPassword(e.target.value)}
                />
              </Field>
              <Field label="New password" htmlFor="pf-new" hint="At least 8 characters.">
                <PasswordInput
                  id="pf-new"
                  autoComplete="new-password"
                  required
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                />
              </Field>
              <Field label="Confirm new password" htmlFor="pf-confirm">
                <PasswordInput
                  id="pf-confirm"
                  autoComplete="new-password"
                  required
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                />
              </Field>
              <FormError>{passwordError || password.error?.message}</FormError>
              <div className="flex items-center gap-3">
                <Button type="submit" variant="primary" busy={password.isPending}>
                  Change password
                </Button>
                <SavedFlash show={passwordSaved} />
              </div>
            </form>
          </Card>

          <Card title="Connected sign-in">
            <div className="flex items-start justify-between gap-3">
              <div>
                <p className="text-sm font-medium text-ink-800">Microsoft account</p>
                <p className="text-[13px] text-ink-500">
                  {user.microsoftLinked
                    ? "Linked — you can sign in with Microsoft."
                    : "Not linked. Signing in with a Microsoft account that matches your email links it automatically."}
                </p>
              </div>
              {user.microsoftLinked ? <Badge tone="ok">Linked</Badge> : <Badge>Not linked</Badge>}
            </div>
          </Card>
        </div>
      </div>
    </div>
  );
}
