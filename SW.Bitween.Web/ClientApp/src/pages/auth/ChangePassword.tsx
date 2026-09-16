import { useState, type FormEvent } from "react";
import { useMutation } from "@tanstack/react-query";
import { api } from "../../api";
import { useSession } from "../../auth/SessionContext";
import { validatePassword } from "../../lib/passwordPolicy";
import { Button, FormError } from "../../components/ui/basics";
import { Field, PasswordInput } from "../../components/ui/forms";
import { AuthLayout } from "./AuthLayout";

/**
 * The one screen an account with an unchanged password can reach.
 *
 * Every installation is seeded with the same administrator, whose password is published in our
 * public repository — so on any installation where nobody changed it, that account was open to
 * anyone who had read the repository. The backend now issues such an account a token that grants
 * nothing at all, which closes the hole but leaves a person signed in to an application where
 * every screen refuses. This is where they are sent instead, and changing the password is what
 * lets them out of it.
 */
export function ChangePasswordPage() {
  const { session, refresh, signOut } = useSession();

  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [error, setError] = useState("");

  const change = useMutation({
    mutationFn: () => api.changePassword(currentPassword, newPassword),
    // The flag lives on the account, so re-reading the session is what clears this screen:
    // the next profile read comes back without it and the guard stops redirecting here.
    onSuccess: () => refresh(),
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    setError("");

    if (newPassword !== confirmPassword) {
      setError("The new passwords don't match.");
      return;
    }
    if (newPassword === currentPassword) {
      setError("Choose a password different from the current one.");
      return;
    }
    // The server rejects a weak password too; checking here saves the round trip.
    const problems = validatePassword(newPassword);
    if (problems.length > 0) {
      setError(problems.join(" "));
      return;
    }
    change.mutate();
  };

  return (
    <AuthLayout>
      <h1 className="text-2xl font-semibold tracking-tight text-ink-900">Choose a password</h1>
      <p className="mt-1.5 text-sm text-ink-500">
        This account still has the password it shipped with, which is publicly known. Pick your own
        to carry on — nothing else is available until you do.
      </p>
      {/*
        Named, because the page is reached by redirect rather than by choice: whoever lands here
        did not ask for it and has no other way to tell which account is being talked about.
      */}
      {session && (
        <p className="mt-3 rounded-md bg-ink-50 px-3 py-2 text-sm text-ink-600">
          Signed in as <span className="font-medium text-ink-900">{session.user.email}</span>
        </p>
      )}

      <form onSubmit={submit} className="mt-7 space-y-4">
        <Field label="Current password" htmlFor="current-password">
          <PasswordInput
            id="current-password"
            autoComplete="current-password"
            value={currentPassword}
            onChange={(e) => setCurrentPassword(e.target.value)}
            required
          />
        </Field>
        <Field label="New password" htmlFor="new-password">
          <PasswordInput
            id="new-password"
            autoComplete="new-password"
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            required
          />
        </Field>
        <Field label="Confirm new password" htmlFor="confirm-password">
          <PasswordInput
            id="confirm-password"
            autoComplete="new-password"
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
            required
          />
        </Field>

        {error && <FormError>{error}</FormError>}
        {change.isError && <FormError>{(change.error as Error).message}</FormError>}

        <Button type="submit" className="w-full" disabled={change.isPending}>
          {change.isPending ? "Saving…" : "Set password"}
        </Button>
      </form>

      {/*
        The way out. Without it this screen is a trap: every route redirects back here, so someone
        who reached it on the wrong account — or who does not know this password — has no way to
        sign in as anyone else short of clearing their cookies.
      */}
      <p className="mt-6 text-center text-sm text-ink-500">
        Not your account?{" "}
        <button
          type="button"
          onClick={() => void signOut()}
          className="font-medium text-ink-900 underline underline-offset-2 hover:text-ink-700"
        >
          Sign in as someone else
        </button>
      </p>
    </AuthLayout>
  );
}
