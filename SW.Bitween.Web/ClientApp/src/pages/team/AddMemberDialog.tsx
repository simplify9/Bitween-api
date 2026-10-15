import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiRequestError, getAppConfig, type InviteResult } from "../../api";
import { Button, FormError } from "../../components/ui/basics";
import { CopyField } from "../../components/ui/CopyField";
import { Checkbox, Field, PasswordInput, TextInput } from "../../components/ui/forms";
import { Dialog } from "../../components/ui/overlays";
import { formatDateTime } from "../../lib/dates";
import { PASSWORD_HINT, validatePassword } from "../../lib/passwordPolicy";
import { keys } from "../../api/queryKeys";

type Mode = "invite" | "password";

function Option({
  checked,
  onChange,
  title,
  children,
}: {
  checked: boolean;
  onChange: () => void;
  title: string;
  children: string;
}) {
  return (
    <label className="flex cursor-pointer items-start gap-2.5">
      <input
        type="radio"
        name="member-access"
        className="mt-1 size-4 shrink-0 accent-crimson-600"
        checked={checked}
        onChange={onChange}
      />
      <span>
        <span className="text-sm font-medium text-ink-900">{title}</span>
        <span className="block text-[13px] text-ink-500">{children}</span>
      </span>
    </label>
  );
}

export function AddMemberDialog({ onClose }: { onClose: () => void }) {
  const queryClient = useQueryClient();
  const roles = useQuery({ queryKey: keys.roles.list, queryFn: () => api.listRoles() });
  const appConfig = useQuery({ queryKey: keys.appConfig, queryFn: getAppConfig });
  const mailOn = Boolean(appConfig.data?.mailServerConfigured);
  // Microsoft or single sign-on only: nobody has a password, so there is none to choose or set.
  const passwordsOff = Boolean(appConfig.data?.disableEmailPasswordLogin);

  const [displayName, setDisplayName] = useState("");
  const [email, setEmail] = useState("");
  const [mode, setMode] = useState<Mode>("invite");
  const [password, setPassword] = useState("");
  const [emailThem, setEmailThem] = useState(true);
  const [roleIds, setRoleIds] = useState<string[]>([]);
  const [passwordError, setPasswordError] = useState("");
  // An invite that wasn't emailed, for whoever added the member to pass on.
  const [handOver, setHandOver] = useState<{ name: string; result: InviteResult } | null>(null);

  const create = useMutation({
    mutationFn: async () => {
      const inviting = !passwordsOff && mode === "invite";
      const user = await api.createUser({
        displayName,
        email,
        roleIds,
        ...(inviting ? { invite: true } : passwordsOff ? {} : { password }),
      });
      if (!(inviting || (passwordsOff && mailOn && emailThem))) return { user, result: null };
      try {
        return { user, result: await api.inviteUser(user.id) };
      } catch (e) {
        // They're added either way; the invite can be sent again from their page.
        const message = e instanceof Error ? e.message : "The invite couldn't be made.";
        return { user, result: { emailed: false, emailError: message, expiresOn: null, link: null } };
      }
    },
    onSuccess: ({ user, result }) => {
      void queryClient.invalidateQueries({ queryKey: keys.users.all });
      void queryClient.invalidateQueries({ queryKey: keys.roles.all });
      if (result && !result.emailed) setHandOver({ name: user.displayName, result });
      else onClose();
    },
  });

  const toggleRole = (id: string) =>
    setRoleIds((prev) => (prev.includes(id) ? prev.filter((r) => r !== id) : [...prev, id]));

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (!passwordsOff && mode === "password") {
      // The server rejects a weak password too; checking here saves the round trip.
      const problems = validatePassword(password);
      if (problems.length > 0) {
        setPasswordError(problems.join(" "));
        return;
      }
    }
    setPasswordError("");
    create.mutate();
  };

  if (handOver) {
    const { name, result } = handOver;
    return (
      <Dialog title={`${name} is added`} onClose={onClose}>
        <div className="space-y-3">
          {result.link ? (
            <>
              {result.emailError && (
                <FormError>The invite email couldn&apos;t be sent: {result.emailError}</FormError>
              )}
              <p className="text-sm text-ink-700">
                Send them this link to choose their password:
              </p>
              <CopyField value={result.link} label="Invite link" />
              <p className="text-[13px] text-ink-500">
                It works once{result.expiresOn ? `, until ${formatDateTime(result.expiresOn)}` : ""}. Bitween
                keeps no copy, so it can&apos;t show it again - you can make a new one from their page.
              </p>
            </>
          ) : (
            <FormError>
              They&apos;re added, but the invite failed: {result.emailError}. Open them in the list to send it
              again.
            </FormError>
          )}
          <div className="flex justify-end">
            <Button variant="primary" onClick={onClose}>
              Done
            </Button>
          </div>
        </div>
      </Dialog>
    );
  }

  return (
    <Dialog title="Add a member" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Field label="Name" htmlFor="member-name">
          <TextInput
            id="member-name"
            required
            autoFocus
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            placeholder="Jane Cooper"
          />
        </Field>

        <Field label="Email" htmlFor="member-email">
          <TextInput
            id="member-email"
            type="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="colleague@company.com"
          />
        </Field>

        {passwordsOff ? (
          mailOn && (
            <Checkbox
              label="Email them that they've been added"
              description="With a link to sign in. They sign in with their company account."
              checked={emailThem}
              onChange={(e) => setEmailThem(e.target.checked)}
            />
          )
        ) : (
          <fieldset className="space-y-2.5">
            <legend className="mb-2 block text-[13px] font-medium text-ink-700">How they get in</legend>
            <Option checked={mode === "invite"} onChange={() => setMode("invite")} title="Send an invite">
              {mailOn
                ? "We email them a link to choose their own password. It works once, for 48 hours."
                : "You get a link to send them, to choose their own password. It works once, for 48 hours. Set up a mail server in Settings to have Bitween email it."}
            </Option>
            <Option checked={mode === "password"} onChange={() => setMode("password")} title="Set a password now">
              You choose it and pass it on to them yourself. They can change it from their profile.
            </Option>
          </fieldset>
        )}

        {!passwordsOff && mode === "password" && (
          <Field label="Password" htmlFor="member-password" hint={PASSWORD_HINT}>
            <PasswordInput
              id="member-password"
              required
              minLength={8}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="At least 8 characters"
            />
          </Field>
        )}

        <fieldset>
          <legend className="mb-2 block text-[13px] font-medium text-ink-700">Roles</legend>
          <div className="max-h-56 space-y-2.5 overflow-y-auto rounded-lg border border-ink-200 p-3">
            {(roles.data ?? []).map((role) => (
              <Checkbox
                key={role.id}
                label={role.name}
                description={role.description}
                checked={roleIds.includes(role.id)}
                onChange={() => toggleRole(role.id)}
              />
            ))}
          </div>
        </fieldset>

        <FormError>
          {passwordError ||
            (create.error instanceof ApiRequestError ? create.error.message : create.error?.message)}
        </FormError>

        <div className="flex justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" busy={create.isPending}>
            {!passwordsOff && mode === "invite" ? (mailOn ? "Add and email invite" : "Add and make invite link") : "Add member"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
