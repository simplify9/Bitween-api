import { useState, type FormEvent } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";
import { useMutation, useQuery } from "@tanstack/react-query";
import { api, ApiRequestError, getAppConfig } from "../../api";
import { keys } from "../../api/queryKeys";
import { Button, FormError, LoadingBlock } from "../../components/ui/basics";
import { Field, PasswordInput } from "../../components/ui/forms";
import { PASSWORD_HINT, validatePassword } from "../../lib/passwordPolicy";
import { AuthLayout } from "./AuthLayout";

/**
 * Where invite and reset emails lead: the member chooses a password, then signs in with it.
 *
 * Reading the link changes nothing, so a mail scanner that opens it first spends nothing; only
 * choosing the password uses it up.
 */
export function SetPasswordPage() {
  const [searchParams] = useSearchParams();
  const token = searchParams.get("token") ?? "";
  const navigate = useNavigate();

  const link = useQuery({
    queryKey: ["password-link", token],
    queryFn: () => api.readPasswordLink(token),
    enabled: token.length > 0,
    retry: false,
  });
  const appConfig = useQuery({ queryKey: keys.appConfig, queryFn: getAppConfig });

  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState("");

  const save = useMutation({
    mutationFn: () => api.usePasswordLink(token, password),
    onSuccess: ({ email }) =>
      navigate("/login", { replace: true, state: { email, notice: "Your password is set. Sign in with it." } }),
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    setError("");
    if (password !== confirm) {
      setError("The passwords don't match.");
      return;
    }
    // The server checks it too; this saves the round trip.
    const problems = validatePassword(password);
    if (problems.length > 0) {
      setError(problems.join(" "));
      return;
    }
    save.mutate();
  };

  if (token && link.isPending) {
    return (
      <AuthLayout>
        <LoadingBlock label="Checking your link…" />
      </AuthLayout>
    );
  }

  // Only the server saying the link is spent means it is. A network error or a busy server says
  // nothing about the link, and sending someone off to ask for a new one would be wrong.
  const spent = link.error instanceof ApiRequestError && link.error.code === "LINK_INVALID";
  if (token && link.isError && !spent) {
    return (
      <AuthLayout>
        <h1 className="text-xl font-semibold tracking-tight text-ink-900">Couldn&apos;t check your link</h1>
        <FormError>{link.error.message}</FormError>
        <Button className="mt-4" busy={link.isFetching} onClick={() => void link.refetch()}>
          Try again
        </Button>
      </AuthLayout>
    );
  }

  if (!token || link.isError || !link.data) {
    return (
      <AuthLayout>
        <h1 className="text-xl font-semibold tracking-tight text-ink-900">This link doesn&apos;t work</h1>
        <p className="mt-2 text-sm text-ink-600">
          It has expired or has already been used. Links work once, and only for a while.
        </p>
        <p className="mt-2 text-sm text-ink-600">
          {appConfig.data?.passwordResetAvailable ? (
            <>
              Ask for a new one with <Link to="/forgot-password" className="font-medium link">Forgot password</Link>,
              or ask your administrator to invite you again.
            </>
          ) : (
            "Ask your administrator for a new one."
          )}
        </p>
        <Link to="/login" className="mt-6 inline-block text-sm font-medium link">
          Back to sign in
        </Link>
      </AuthLayout>
    );
  }

  const invite = link.data.purpose === "Invite";

  return (
    <AuthLayout>
      <h1 className="text-xl font-semibold tracking-tight text-ink-900">
        {invite ? `Welcome, ${link.data.name}` : "Choose a new password"}
      </h1>
      <p className="mt-1 text-sm text-ink-500">
        {invite
          ? "Choose a password to finish setting up your account."
          : "Once it's set, you're signed out everywhere else."}
      </p>
      <p className="mt-3 rounded-md bg-ink-50 px-3 py-2 text-sm text-ink-600">
        For <span className="font-medium text-ink-900">{link.data.email}</span>
      </p>

      <form onSubmit={submit} className="mt-6 space-y-4">
        <Field label="New password" htmlFor="set-password" hint={PASSWORD_HINT}>
          <PasswordInput
            id="set-password"
            autoComplete="new-password"
            required
            autoFocus
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </Field>
        <Field label="Confirm password" htmlFor="set-password-confirm">
          <PasswordInput
            id="set-password-confirm"
            autoComplete="new-password"
            required
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
          />
        </Field>
        <FormError>{error || save.error?.message}</FormError>
        <Button type="submit" variant="primary" busy={save.isPending} className="w-full">
          Set password
        </Button>
      </form>
    </AuthLayout>
  );
}
